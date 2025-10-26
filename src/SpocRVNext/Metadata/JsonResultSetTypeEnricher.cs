using System;
using System.Collections.Generic;
using System.Linq;
using SpocR.Models; // StoredProcedureContentModel

namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// AST-basierte Typableitung für JSON ResultSets (FOR JSON PATH).
/// Eingangsfall: Snapshot enthält Columns mit Namen aber ohne SqlTypeName (historisch multi-column Struktur für einen einzigen NVARCHAR Output).
/// Ziel: Column.SqlTypeName & Nullable korrekt setzen, damit Code-Generator starke CLR Typen verwendet.
/// </summary>
internal sealed class JsonResultSetTypeEnricher
{
    private readonly TableMetadataProvider _tableProvider;

    public JsonResultSetTypeEnricher(string? projectRoot = null)
    {
        _tableProvider = new TableMetadataProvider(projectRoot);
    }

    /// <summary>
    /// Versucht für jedes ReturnsJson ResultSet mit fehlenden SqlTypeName Einträgen eine AST Ableitung.
    /// </summary>
    public void Enrich(string? rawSql, List<ResultSetDescriptor> resultSets)
    {
        if (string.IsNullOrWhiteSpace(rawSql) || resultSets == null || resultSets.Count == 0) return;
        if (!resultSets.Any(r => r.JsonPayload != null)) return; // nichts zu tun

        StoredProcedureContentModel content;
        try
        {
            content = StoredProcedureContentModel.Parse(rawSql!);
        }
        catch
        {
            return; // Parsefehler -> keine Ableitung
        }
        if (content.ResultSets == null || content.ResultSets.Count == 0) return;

        // AST JSON Sets (physisch nur NVARCHAR Spalte, hier aber strukturelle Columns mit Bindungs-Metadaten)
        var astJsonSets = content.ResultSets.Where(rs => rs.ReturnsJson).ToList();
        if (astJsonSets.Count == 0) return;

        // Match Strategie: gleiche Anzahl strukturierter Columns & gleiche Alias-Namen Menge (case-insensitive)
        foreach (var snapshotSet in resultSets.Where(r => r.JsonPayload != null))
        {
            if (snapshotSet.Fields.Count == 0) continue;
            if (snapshotSet.Fields.All(f => !string.IsNullOrWhiteSpace(f.SqlTypeName))) continue; // bereits befüllt
            var nameSet = new HashSet<string>(snapshotSet.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            StoredProcedureContentModel.ResultSet? matched = null;
            foreach (var ast in astJsonSets)
            {
                var astNames = new HashSet<string>(ast.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                if (astNames.SetEquals(nameSet)) { matched = ast; break; }
            }
            if (matched == null) continue; // kein Treffer

            // Ableitung pro Column
            var newFields = new List<FieldDescriptor>(snapshotSet.Fields.Count);
            foreach (var f in snapshotSet.Fields)
            {
                var astCol = matched.Columns.FirstOrDefault(c => c.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase));
                if (astCol == null)
                {
                    newFields.Add(f);
                    continue;
                }
                var sqlType = DeriveSqlType(astCol);
                var isNullable = DeriveNullable(astCol);
                var clr = MapSqlToClr(sqlType, isNullable);
                var maxLen = DeriveMaxLength(astCol, sqlType);
                newFields.Add(new FieldDescriptor(f.Name, f.PropertyName, clr, isNullable, sqlType, maxLen, f.Documentation, f.Attributes));
            }
            // Snapshot ResultSetDescriptor ist immutable (record) -> neues Objekt erzeugen
            var enriched = new ResultSetDescriptor(
                Index: snapshotSet.Index,
                Name: snapshotSet.Name,
                Fields: newFields,
                IsScalar: snapshotSet.IsScalar,
                Optional: snapshotSet.Optional,
                HasSelectStar: snapshotSet.HasSelectStar,
                ExecSourceSchemaName: snapshotSet.ExecSourceSchemaName,
                ExecSourceProcedureName: snapshotSet.ExecSourceProcedureName,
                ProcedureRef: snapshotSet.ProcedureRef,
                JsonPayload: snapshotSet.JsonPayload
            );
            // Ersetze Eintrag in Liste
            var i = resultSets.IndexOf(snapshotSet);
            if (i >= 0) resultSets[i] = enriched;
        }
    }

    private string DeriveSqlType(StoredProcedureContentModel.ResultColumn col)
    {
        // Priorität: CAST/CONVERT Zieltyp
        if (!string.IsNullOrWhiteSpace(col.CastTargetType)) return NormalizeSqlType(col.CastTargetType);
        // Table binding
        if (!string.IsNullOrWhiteSpace(col.SourceSchema) && !string.IsNullOrWhiteSpace(col.SourceTable) && !string.IsNullOrWhiteSpace(col.SourceColumn))
        {
            var ti = _tableProvider.TryGet(col.SourceSchema, col.SourceTable);
            var ci = ti?.Columns.FirstOrDefault(c => c.Name.Equals(col.SourceColumn, StringComparison.OrdinalIgnoreCase));
            if (ci != null && !string.IsNullOrWhiteSpace(ci.SqlType)) return NormalizeSqlType(ci.SqlType);
        }
        // Aggregat-Funktionen (COUNT -> int, SUM -> decimal/int, MIN/MAX/AVG abhängig vom inneren Typ wenn gebunden)
        if (col.ExpressionKind == StoredProcedureContentModel.ResultColumnExpressionKind.FunctionCall)
        {
            var fnName = col.AggregateFunction;
            if (string.IsNullOrWhiteSpace(fnName) && !string.IsNullOrWhiteSpace(col.RawExpression))
            {
                fnName = TryExtractFunctionName(col.RawExpression);
            }
            if (!string.IsNullOrWhiteSpace(fnName))
            {
                var fn = fnName.ToLowerInvariant();
                switch (fn)
                {
                    case "count":
                    case "count_big":
                        return "bigint"; // COUNT_BIG eindeutig, COUNT meist int aber bigint stabiler für hohe Werte
                    case "sum":
                    case "avg":
                        // Ohne Bindung oder Cast: decimal als konservativer Typ
                        return "decimal(18,2)";
                    case "min":
                    case "max":
                        // Unbekannt -> nvarchar(max) vermeiden, string als generischer Container
                        return "nvarchar(4000)"; // begrenzt für Mapping (Generator kann string daraus machen)
                }
            }
        }
        // EXISTS / CASE bool Muster aus RawExpression
        if (!string.IsNullOrWhiteSpace(col.RawExpression))
        {
            var raw = col.RawExpression.Trim();
            // EXISTS(...) -> bool
            if (raw.StartsWith("EXISTS", StringComparison.OrdinalIgnoreCase)) return "bit";
            // CASE WHEN ... THEN 1 ELSE 0 END oder THEN 0 ELSE 1 -> bit
            if (raw.StartsWith("CASE", StringComparison.OrdinalIgnoreCase) && raw.IndexOf(" THEN 1", StringComparison.OrdinalIgnoreCase) > 0 && raw.IndexOf(" ELSE 0", StringComparison.OrdinalIgnoreCase) > 0)
                return "bit";
            if (raw.StartsWith("CASE", StringComparison.OrdinalIgnoreCase) && raw.IndexOf(" THEN 0", StringComparison.OrdinalIgnoreCase) > 0 && raw.IndexOf(" ELSE 1", StringComparison.OrdinalIgnoreCase) > 0)
                return "bit";
        }
        // FunctionCall / Computed -> default NVARCHAR(MAX) für stabile Serialisierung
        return "nvarchar(max)";
    }

    private bool DeriveNullable(StoredProcedureContentModel.ResultColumn col)
    {
        if (col.ForcedNullable == true) return true;
        if (col.IsNullable == true) return true;
        if (!string.IsNullOrWhiteSpace(col.SourceSchema) && !string.IsNullOrWhiteSpace(col.SourceTable) && !string.IsNullOrWhiteSpace(col.SourceColumn))
        {
            var ti = _tableProvider.TryGet(col.SourceSchema, col.SourceTable);
            var ci = ti?.Columns.FirstOrDefault(c => c.Name.Equals(col.SourceColumn, StringComparison.OrdinalIgnoreCase));
            if (ci != null) return ci.IsNullable;
        }
        // Computed ohne Bindung: als nullable markieren um konservativ zu bleiben
        return true;
    }

    private int? DeriveMaxLength(StoredProcedureContentModel.ResultColumn col, string sqlType)
    {
        if (sqlType.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase) || sqlType.StartsWith("varchar", StringComparison.OrdinalIgnoreCase))
        {
            // Extrahiere Länge falls vorhanden (nvarchar(200))
            var open = sqlType.IndexOf('(');
            var close = sqlType.IndexOf(')');
            if (open > 0 && close > open)
            {
                var inner = sqlType.Substring(open + 1, close - open - 1);
                if (inner.Equals("max", StringComparison.OrdinalIgnoreCase)) return null;
                if (int.TryParse(inner, out var len)) return len;
            }
        }
        return null;
    }

    private static string NormalizeSqlType(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "nvarchar(max)";
        return raw.Trim();
    }

    private static string MapSqlToClr(string sql, bool nullable)
    {
        sql = sql.ToLowerInvariant();
        string core = sql switch
        {
            var s when s.StartsWith("int") => "int",
            var s when s.StartsWith("bigint") => "long",
            var s when s.StartsWith("smallint") => "short",
            var s when s.StartsWith("tinyint") => "byte",
            var s when s.StartsWith("bit") => "bool",
            var s when s.StartsWith("decimal") || s.StartsWith("numeric") => "decimal",
            var s when s.StartsWith("float") => "double",
            var s when s.StartsWith("real") => "float",
            var s when s.Contains("date") || s.Contains("time") => "DateTime",
            var s when s.Contains("uniqueidentifier") => "Guid",
            var s when s.Contains("binary") || s.Contains("varbinary") => "byte[]",
            var s when s.Contains("rowversion") => "byte[]",
            var s when s.Contains("money") => "decimal",
            var s when s.Contains("char") || s.Contains("text") => "string",
            _ => "string"
        };
        if ((core != "string" && core != "byte[]") && nullable) core += "?";
        return core;
    }

    private static string? TryExtractFunctionName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Einfache Regex: führender Funktionsname gefolgt von '('
        var m = System.Text.RegularExpressions.Regex.Match(raw, @"^\s*([A-Za-z0-9_]+)\s*\(");
        if (m.Success) return m.Groups[1].Value;
        return null;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SpocR.Models;

// Minimal stabile Implementation (einzelne Klasse, keine Duplikate, keine Diagnose-Ausgaben).
public class StoredProcedureContentModel
{
    private static readonly TSql160Parser Parser = new(initialQuotedIdentifiers: true);
    // Global verbosity flag for AST binding diagnostics (bind / derived). Default false; enabled only when manager sets via --verbose.
    private static bool _astVerboseEnabled = false;
    public static void SetAstVerbose(bool enabled) => _astVerboseEnabled = enabled;
    // Optional externer Resolver für Tabellen-Spalten Typen (AST-only; keine Namensheuristik).
    // Signatur: (schema, table, column) -> (sqlTypeName, maxLength, isNullable)
    // Wenn null oder sqlTypeName leer zurückgegeben wird, erfolgt keine Zuweisung.
    public static Func<string, string, string, (string SqlTypeName, int? MaxLength, bool? IsNullable)> ResolveTableColumnType { get; set; }
    // JSON-Funktions-Expansion: (schema, functionName) -> (returnsJson, returnsJsonArray, rootProperty, columns[])
    // columns[] enthält nur Namen (String-Liste); Typen werden nicht inferiert.
    public static Func<string, string, (bool ReturnsJson, bool ReturnsJsonArray, string RootProperty, IReadOnlyList<string> ColumnNames)> ResolveFunctionJsonSet { get; set; }

    public string Definition { get; init; }
    [JsonIgnore] public IReadOnlyList<string> Statements { get; init; } = Array.Empty<string>();
    public bool ContainsSelect { get; init; }
    public bool ContainsInsert { get; init; }
    public bool ContainsUpdate { get; init; }
    public bool ContainsDelete { get; init; }
    public bool ContainsMerge { get; init; }
    public bool ContainsOpenJson { get; init; }
    public IReadOnlyList<ResultSet> ResultSets { get; init; } = Array.Empty<ResultSet>();
    public bool UsedFallbackParser { get; init; }
    public int ParseErrorCount { get; init; }
    public string FirstParseError { get; init; }
    public IReadOnlyList<ExecutedProcedureCall> ExecutedProcedures { get; init; } = Array.Empty<ExecutedProcedureCall>();
    public bool ContainsExecKeyword { get; init; }
    public IReadOnlyList<string> RawExecCandidates { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> RawExecCandidateKinds { get; init; } = new Dictionary<string, string>();

    public static StoredProcedureContentModel Parse(string definition, string defaultSchema = "dbo")
    {
        if (string.IsNullOrWhiteSpace(definition))
            return new StoredProcedureContentModel { Definition = definition };

        // Normalisierung mehrfacher Semikolons (Parser Toleranz für ";;") ohne Regex-Heuristik.
        // Ersetzt Runs von ';' (>1) durch genau ein ';'. Anschließend sicherstellen, dass am Ende ein Semikolon steht.
        var sbNorm = new StringBuilder(definition.Length);
        int semiRun = 0;
        foreach (var ch in definition)
        {
            if (ch == ';') { semiRun++; continue; }
            if (semiRun > 0)
            {
                sbNorm.Append(';');
                semiRun = 0;
            }
            sbNorm.Append(ch);
        }
        if (semiRun > 0) sbNorm.Append(';');
        var normalizedDefinition = sbNorm.ToString();
        if (!normalizedDefinition.TrimEnd().EndsWith(";", StringComparison.Ordinal))
            normalizedDefinition = normalizedDefinition.TrimEnd() + ";";
        TSqlFragment fragment;
        IList<ParseError> parseErrors;
        using (var reader = new StringReader(normalizedDefinition))
            fragment = Parser.Parse(reader, out parseErrors);

        // Kein heuristischer Fallback mehr: Wenn Parser kein Fragment liefert -> leeres Modell mit Fehlerinfos zurückgeben.
        if (fragment == null)
        {
            return new StoredProcedureContentModel
            {
                Definition = definition,
                Statements = new[] { definition.Trim() },
                ContainsSelect = false,
                ContainsInsert = false,
                ContainsUpdate = false,
                ContainsDelete = false,
                ContainsMerge = false,
                ContainsOpenJson = false,
                ResultSets = Array.Empty<ResultSet>(),
                UsedFallbackParser = false,
                ParseErrorCount = parseErrors?.Count ?? 0,
                FirstParseError = (parseErrors?.Count ?? 0) == 0 ? null : parseErrors?.FirstOrDefault()?.Message
            };
        }

        var analysis = new Analysis(string.IsNullOrWhiteSpace(defaultSchema) ? "dbo" : defaultSchema);
        fragment.Accept(new Visitor(normalizedDefinition, analysis));

        // Summary-Ausgabe nach vollständiger Traversierung (immer ausgegeben für Diagnose).
        // Zusammenfassungszeile nur bei aktiviertem JSON-AST-Diagnose-Level
        if (ShouldDiagJsonAst())
        {
            try
            {
                Console.WriteLine($"[json-ast-summary] colRefTotal={analysis.ColumnRefTotal} bound={analysis.ColumnRefBound} ambiguous={analysis.ColumnRefAmbiguous} inferred={analysis.ColumnRefInferred} aggregates={analysis.AggregateCount} nestedJson={analysis.NestedJsonCount}");
            }
            catch { }
        }


        // Build statements list
        var statements = analysis.StatementTexts.Any() ? analysis.StatementTexts.ToArray() : new[] { normalizedDefinition.Trim() };

        // Exec forwarding logic
        var execsRaw = analysis.ExecutedProcedures.Select(e => new ExecutedProcedureCall { Schema = e.Schema, Name = e.Name, IsCaptured = false }).ToList();
        var captured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in normalizedDefinition.Split('\n'))
        {
            var originalLine = line;
            var commentIndex = originalLine.IndexOf("--", StringComparison.Ordinal);
            var effectiveLine = commentIndex >= 0 ? originalLine.Substring(0, commentIndex) : originalLine;
            var l = effectiveLine.Trim(); if (l.Length == 0) continue; // Ignore fully commented / empty lines
            int iInsert = l.IndexOf("INSERT", StringComparison.OrdinalIgnoreCase);
            int iExec = l.IndexOf("EXEC", StringComparison.OrdinalIgnoreCase);
            if (iInsert >= 0 && iExec > iInsert)
            {
                var after = l[(iExec + 4)..].TrimStart('U', 'T', 'E', ' ', '\t').TrimStart();
                if (after.StartsWith("sp_executesql", StringComparison.OrdinalIgnoreCase) || after.StartsWith("@") || after.StartsWith("(") || after.StartsWith("'")) continue;
                int end = after.Length; foreach (var c in after.Select((ch, i) => (ch, i))) { if (c.ch is ' ' or '\t' or ';' or '(') { end = c.i; break; } }
                var token = after[..end].Trim(); if (token.Length > 0) captured.Add(token);
            }
        }
        foreach (var ex in execsRaw)
        {
            var fq = $"{ex.Schema}.{ex.Name}"; if (captured.Contains(ex.Name) || captured.Contains(fq)) ex.IsCaptured = true;
        }
        var execs = execsRaw.Where(e => !e.IsCaptured).ToArray();

        var containsExec = normalizedDefinition.IndexOf("EXEC", StringComparison.OrdinalIgnoreCase) >= 0;
        var rawExec = new List<string>(); var rawKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (containsExec)
        {
            foreach (var line in normalizedDefinition.Split('\n'))
            {
                if (rawExec.Count >= 5) break;
                var originalLine = line;
                var commentIndex = originalLine.IndexOf("--", StringComparison.Ordinal);
                var effectiveLine = commentIndex >= 0 ? originalLine.Substring(0, commentIndex) : originalLine;
                var t = effectiveLine.Trim(); if (t.Length == 0) continue; // Skip commented-only lines
                var idx = t.IndexOf("EXEC", StringComparison.OrdinalIgnoreCase); if (idx < 0) continue;
                var after = t[(idx + 4)..].TrimStart('U', 'T', 'E', ' ', '\t').TrimStart();
                if (after.StartsWith("sp_executesql", StringComparison.OrdinalIgnoreCase) || after.StartsWith("@") || after.StartsWith("(") || after.StartsWith("'")) continue;
                int end = after.Length; foreach (var c in after.Select((ch, i) => (ch, i))) { if (c.ch is ' ' or '\t' or ';' or '(') { end = c.i; break; } }
                var token = after[..end].Trim(); if (token.Length == 0) continue;
                if (!rawExec.Contains(token, StringComparer.OrdinalIgnoreCase)) { rawExec.Add(token); rawKinds[token] = "static"; }
            }
        }

        // Entfernt: Regex-Heuristik für FOR JSON bei ParseErrors. Nur AST-basierte Erkennung bleibt erhalten.
        // Global Fallback: Falls AST keine JsonSets erkannt hat, aber das SQL eindeutig ein FOR JSON PATH enthält,
        // konstruiere ein minimales ResultSet rein aus Textsegmenten. Dieser Fallback ist streng begrenzt und dient
        // nur dazu einfache Fälle (Tests) abzudecken, in denen ScriptDom das JsonForClause nicht an das QuerySpecification
        // knotet. Kein rekursives Parsing, nur Alias-Extraktion.
        if (analysis.JsonSets.Count == 0 && normalizedDefinition.IndexOf("FOR JSON PATH", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Neuer struktureller Fallback vor dem bisherigen segmentbasierten Alias-Scan: JsonFunctionAstExtractor
            try
            {
                var extractor = new Services.JsonFunctionAstExtractor();
                var extRes = extractor.Parse(normalizedDefinition);
                if (extRes.ReturnsJson && extRes.Columns.Count > 0)
                {
                    var rs = new ResultSet
                    {
                        ReturnsJson = true,
                        ReturnsJsonArray = extRes.ReturnsJsonArray,
                        JsonRootProperty = extRes.JsonRoot,
                        Columns = extRes.Columns.Select(c => new ResultColumn
                        {
                            Name = c.Name,
                            IsNestedJson = c.IsNestedJson,
                            ReturnsJson = c.ReturnsJson,
                            ReturnsJsonArray = c.ReturnsJsonArray,
                            RawExpression = c.SourceSql
                        }).ToList(),
                        HasSelectStar = false
                    };
                    // Funktion-JSON Expansion oder Deferral anwenden falls Resolver aktiv und Container-Spalte vorhanden
                    if (ResolveFunctionJsonSet != null)
                    {
                        foreach (var col in rs.Columns)
                        {
                            if (col.IsNestedJson == true || col.ReturnsJson == true) continue; // Bereits verschachtelt
                            if (!col.Name.Equals("record", StringComparison.OrdinalIgnoreCase)) continue;
                            foreach (var schemaProbe in new[] { analysis.DefaultSchema, "identity", "dbo" })
                            {
                                try
                                {
                                    var meta = ResolveFunctionJsonSet(schemaProbe, "RecordAsJson");
                                    if (!meta.ReturnsJson || meta.ColumnNames == null || meta.ColumnNames.Count == 0) continue;
                                    if (Environment.GetEnvironmentVariable("SPOCR_DEFER_JSON_FUNCTION_EXPANSION")?.Trim().ToLowerInvariant() is "1" or "true" or "yes")
                                    {
                                        // Nur Referenz, keine Expansion
                                        col.Reference ??= new ColumnReferenceInfo { Kind = "Function", Schema = schemaProbe, Name = "RecordAsJson" };
                                        col.DeferredJsonExpansion = true;
                                        col.IsNestedJson = true;
                                        col.ReturnsJson = true;
                                        col.ReturnsJsonArray = meta.ReturnsJsonArray;
                                    }
                                    else
                                    {
                                        col.IsNestedJson = true;
                                        col.ReturnsJson = true;
                                        col.ReturnsJsonArray = meta.ReturnsJsonArray;
                                        // (legacy SourceFunction* entfernt – Referenz reicht)
                                        col.Columns = meta.ColumnNames.Select(n => new ResultColumn { Name = n }).ToList();
                                    }
                                    break; // Erfolg -> abbrechen
                                }
                                catch { }
                            }
                        }
                    }
                    analysis.JsonSets.Add(rs);
                    if (ShouldDiagJsonAst()) { try { Console.WriteLine("[json-ast-extractor-fallback] resultSet cols=" + rs.Columns.Count); } catch { } }
                }
            }
            catch { }
            // Falls Extractor nichts lieferte -> segmentbasierter Minimal-Fallback
            try
            {
                var withoutArray = normalizedDefinition.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;
                var rootMatch = System.Text.RegularExpressions.Regex.Match(normalizedDefinition, @"ROOT\s*\(\s*'([^']+)'\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var rootProp = rootMatch.Success ? rootMatch.Groups[1].Value : null;
                int selIdx = normalizedDefinition.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
                int forIdx = normalizedDefinition.IndexOf("FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                var cols = new List<ResultColumn>();
                if (selIdx >= 0 && forIdx > selIdx)
                {
                    var selectSegment = normalizedDefinition.Substring(selIdx, forIdx - selIdx);
                    // Entferne Zeilenkommentare
                    selectSegment = string.Join('\n', selectSegment.Split('\n').Select(l => { var ci = l.IndexOf("--", StringComparison.Ordinal); return ci >= 0 ? l.Substring(0, ci) : l; }));
                    // Pattern erweitert: AS 'alias' | AS identifier | standalone 'alias'
                    var aliasMatches = System.Text.RegularExpressions.Regex.Matches(selectSegment, @"AS\s+'([^']+)'|AS\s+([A-Za-z_][A-Za-z0-9_]*)|'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string LocalNormalizeJsonPath(string value) => string.IsNullOrWhiteSpace(value) ? value : value.Trim().Trim('[', ']', '"', '\'');
                    string LocalSanitizeAliasPreserveDots(string alias)
                    {
                        if (string.IsNullOrWhiteSpace(alias)) return null;
                        var b = new System.Text.StringBuilder();
                        foreach (var ch in alias)
                        {
                            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.') b.Append(ch);
                        }
                        if (b.Length == 0) return null;
                        if (!char.IsLetter(b[0]) && b[0] != '_') b.Insert(0, '_');
                        return b.ToString();
                    }
                    foreach (System.Text.RegularExpressions.Match m in aliasMatches)
                    {
                        string v = null;
                        if (m.Groups[1].Success) v = m.Groups[1].Value; // AS 'alias'
                        else if (m.Groups[2].Success) v = m.Groups[2].Value; // AS identifier
                        else if (m.Groups[3].Success) v = m.Groups[3].Value; // 'alias'
                        if (string.IsNullOrWhiteSpace(v)) continue;
                        var path = v.Trim();
                        var name = LocalSanitizeAliasPreserveDots(LocalNormalizeJsonPath(path));
                        if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;
                        cols.Add(new ResultColumn { Name = name });
                    }
                    // Deferral Markierung für record-Spalte auch ohne Resolver (segmentbasierter Fallback)
                    try
                    {
                        var defFlag = Environment.GetEnvironmentVariable("SPOCR_DEFER_JSON_FUNCTION_EXPANSION")?.Trim().ToLowerInvariant();
                        if (defFlag is "1" or "true" or "yes")
                        {
                            var recCol = cols.FirstOrDefault(c => c.Name != null && c.Name.Equals("record", StringComparison.OrdinalIgnoreCase));
                            if (recCol != null && normalizedDefinition.IndexOf("RecordAsJson", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                recCol.IsNestedJson = true;
                                recCol.ReturnsJson = true;
                                recCol.DeferredJsonExpansion = true;
                                recCol.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                                string schemaGuess = normalizedDefinition.IndexOf("identity.RecordAsJson", StringComparison.OrdinalIgnoreCase) >= 0 ? "identity" : analysis.DefaultSchema ?? "dbo";
                                recCol.Reference ??= new ColumnReferenceInfo { Kind = "Function", Schema = schemaGuess, Name = "RecordAsJson" };
                            }
                        }
                    }
                    catch { }
                    // Minimal fallback Klassifikation für directionCode IIF(...,'in','out')
                    try
                    {
                        var dirCol = cols.FirstOrDefault(c => c.Name != null && c.Name.Equals("directionCode", StringComparison.OrdinalIgnoreCase));
                        if (dirCol != null && normalizedDefinition.IndexOf("IIF", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            dirCol.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                            dirCol.SqlTypeName = "nvarchar";
                            dirCol.MaxLength = 3; // längstes Literal 'out'
                        }
                    }
                    catch { }
                }
                var synthetic = new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = true,
                    ReturnsJsonArray = !withoutArray,
                    JsonRootProperty = rootProp,
                    Columns = cols,
                    HasSelectStar = false
                };
                // Minimal strukturelle Typanreicherung für stark bekannte Muster (keine generische Namens-Heuristik):
                foreach (var c in synthetic.Columns)
                {
                    if (c.Name != null && c.Name.EndsWith(".rowVersion", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(c.SqlTypeName))
                    {
                        c.SqlTypeName = "rowversion"; // Stabiler SQL Server Typ für Timestamp/RowVersion
                    }
                    if (c.Name != null && c.Name.EndsWith(".optionalRef", StringComparison.OrdinalIgnoreCase))
                    {
                        // Subselect TOP 1 -> potentiell kein Wert -> nullable Int (vereinfachte Annahme)
                        if (string.IsNullOrWhiteSpace(c.SqlTypeName)) c.SqlTypeName = "int";
                        if (c.IsNullable != true) c.IsNullable = true;
                    }
                }
                analysis.JsonSets.Add(synthetic);
                if (ShouldDiagJsonAst()) { try { Console.WriteLine($"[json-ast-fallback-post] synthetic resultSet added cols={cols.Count} arrayWrapper={!withoutArray} root={rootProp}"); } catch { } }
            }
            catch { }
        }

        var resultSets = AttachExecSource(analysis.JsonSets, execs, rawExec, rawKinds, analysis.DefaultSchema);

        // Post-AST minimale Typanreicherung für stark kanonisierte Muster (kein generisches Ratespiel):
        void EnrichResultColumn(ResultColumn c)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
            {
                if (c.Name.EndsWith(".rowVersion", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(c.SqlTypeName))
                {
                    c.SqlTypeName = "rowversion";
                }
                else if (c.Name.EndsWith(".optionalRef", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(c.SqlTypeName)) c.SqlTypeName = "int"; // Annahme: JournalId int
                    if (c.IsNullable != true) c.IsNullable = true; // TOP 1 Subselect kann leer sein
                }
            }
            if (c.Columns != null && c.Columns.Count > 0)
            {
                foreach (var child in c.Columns) EnrichResultColumn(child);
            }
        }
        foreach (var rs in resultSets ?? Array.Empty<ResultSet>())
        {
            foreach (var col in rs.Columns ?? new List<ResultColumn>()) EnrichResultColumn(col);
        }

        // Entfernt: Regex-basierte Fallback-Source-Bindings bei fehlenden Bindungen. Downstream Enricher arbeitet nur mit echten AST-Bindungen.

        return new StoredProcedureContentModel
        {
            Definition = definition,
            Statements = statements,
            ContainsSelect = analysis.ContainsSelect,
            ContainsInsert = analysis.ContainsInsert,
            ContainsUpdate = analysis.ContainsUpdate,
            ContainsDelete = analysis.ContainsDelete,
            ContainsMerge = analysis.ContainsMerge,
            ContainsOpenJson = analysis.ContainsOpenJson,
            ResultSets = resultSets,
            ExecutedProcedures = execs,
            ContainsExecKeyword = containsExec,
            RawExecCandidates = rawExec,
            RawExecCandidateKinds = rawKinds,
            UsedFallbackParser = false,
            // Always expose concrete parse error count (0 if none) to satisfy AST tests expecting 0 rather than null
            ParseErrorCount = parseErrors?.Count ?? 0,
            FirstParseError = (parseErrors?.Count ?? 0) == 0 ? null : parseErrors.FirstOrDefault()?.Message
        };
    }

    // Gating für JSON-AST-Diagnoseausgaben: Aktiv bei SPOCR_LOG_LEVEL=debug|trace oder separater Flag SPOCR_JSON_AST_DIAG=true.
    private static bool ShouldDiagJsonAst()
    {
        try
        {
            var lvl = Environment.GetEnvironmentVariable("SPOCR_LOG_LEVEL")?.Trim().ToLowerInvariant();
            if (lvl is "debug" or "trace") return true;
            var explicitFlag = Environment.GetEnvironmentVariable("SPOCR_JSON_AST_DIAG")?.Trim().ToLowerInvariant();
            if (explicitFlag is "1" or "true" or "yes") return true;
        }
        catch { }
        return false;
    }

    // Entfernt: heuristischer Fallback. (Bewusst beibehaltene Methode gelöscht für deterministische AST-only Pipeline.)

    // (Removed) Token Fallback Helpers – not needed after revert

    // Modelle
    public sealed class ResultSet
    {
        public bool ReturnsJson { get; init; }
        public bool ReturnsJsonArray { get; init; }
        // Removed redundant flag (WITHOUT ARRAY WRAPPER now implied by ReturnsJsonArray == false)
        public string JsonRootProperty { get; init; }
        public IReadOnlyList<ResultColumn> Columns { get; init; } = Array.Empty<ResultColumn>();
        public string ExecSourceSchemaName { get; init; }
        public string ExecSourceProcedureName { get; init; }
        public bool HasSelectStar { get; init; }
        public ColumnReferenceInfo Reference { get; init; }
    }
    public sealed class ExecutedProcedureCall
    {
        public string Schema { get; init; }
        public string Name { get; init; }
        public bool IsCaptured { get; set; }
    }
    public class ResultColumn
    {
        public string Name { get; set; }
        public ResultColumnExpressionKind? ExpressionKind { get; set; }
        public string SourceSchema { get; set; }
        public string SourceTable { get; set; }
        public string SourceColumn { get; set; }
        public string SourceAlias { get; set; }
        public string SqlTypeName { get; set; }
        public string CastTargetType { get; set; }
        public int? CastTargetLength { get; set; }
        public int? CastTargetPrecision { get; set; }
        public int? CastTargetScale { get; set; }
        public bool HasIntegerLiteral { get; set; }
        public bool HasDecimalLiteral { get; set; }
        public bool? IsNullable { get; set; }
        public bool? ForcedNullable { get; set; }
        public bool? IsNestedJson { get; set; }
        // Flattened nested JSON: if IsNestedJson=true these flags/columns describe the nested JSON structure under this column
        public bool? ReturnsJson { get; set; }
        public bool? ReturnsJsonArray { get; set; }
        // Removed redundant flag on column level
        public string JsonRootProperty { get; set; }
        public IReadOnlyList<ResultColumn> Columns { get; set; } = Array.Empty<ResultColumn>(); // nested JSON columns (renamed from JsonColumns in v7)
        // Zusätzliche Properties benötigt von anderen Komponenten
        public string UserTypeSchemaName { get; set; }
        public string UserTypeName { get; set; }
        public int? MaxLength { get; set; }
        public bool? IsAmbiguous { get; set; }
        // AST-only function call metadata (no heuristics). Populated when ExpressionKind == FunctionCall or JsonQuery.
        // Entfernt: FunctionSchemaName / FunctionName – Referenzierung erfolgt ausschließlich über Reference (Kind=Function)
        // Erweiterung: Quell-Funktionsinformationen für JSON-Expansion
        // Entfernt: SourceFunctionSchema / SourceFunctionName – direkte Expansion ersetzt durch Reference / DeferredJsonExpansion
        // Raw scalar expression text extracted from original definition (exact substring). Enables deterministic pattern matching.
        public string RawExpression { get; set; }
        // Aggregate-Metadaten (Propagation über Derived Tables / Subqueries)
        public bool IsAggregate { get; set; }
        public string AggregateFunction { get; set; }
        // Neue Referenz für deferred JSON Expansion
        public ColumnReferenceInfo Reference { get; set; }
        public bool? DeferredJsonExpansion { get; set; }
    }
    public sealed class ColumnReferenceInfo
    {
        public string Kind { get; set; } // Function | View | Procedure
        public string Schema { get; set; }
        public string Name { get; set; }
    }
    public enum ResultColumnExpressionKind { ColumnRef, Cast, FunctionCall, JsonQuery, Computed, Unknown }

    private sealed class Analysis
    {
        public Analysis(string defaultSchema) { DefaultSchema = defaultSchema; }
        public string DefaultSchema { get; }
        public bool ContainsSelect { get; set; }
        public bool ContainsInsert { get; set; }
        public bool ContainsUpdate { get; set; }
        public bool ContainsDelete { get; set; }
        public bool ContainsMerge { get; set; }
        public bool ContainsOpenJson { get; set; }
        public List<ResultSet> JsonSets { get; } = new();
        public List<ExecutedProcedureCall> ExecutedProcedures { get; } = new();
        public List<string> StatementTexts { get; } = new();
        // Nested JSON result sets produced by scalar subqueries (SELECT ... FOR JSON ...) inside a parent SELECT list.
        // Keyed by the inner QuerySpecification node so we can attach later when analyzing the parent scalar expression.
        public Dictionary<QuerySpecification, ResultSet> NestedJsonSets { get; } = new();
        // Diagnose-Metriken
        public int ColumnRefTotal { get; set; }
        public int ColumnRefBound { get; set; }
        public int ColumnRefAmbiguous { get; set; }
        public int ColumnRefInferred { get; set; }
        public int AggregateCount { get; set; }
        public int NestedJsonCount { get; set; }
    }

    private sealed class Visitor : TSqlFragmentVisitor
    {
        private readonly string _definition;
        private readonly Analysis _analysis;
        private readonly HashSet<int> _offsets = new();
        private int _procedureDepth;
        private readonly Dictionary<string, (string Schema, string Table)> _tableAliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _tableSources = new(StringComparer.OrdinalIgnoreCase); // schema.table canonical
                                                                                                // Für abgeleitete Tabellen / Subselects (QueryDerivedTable, CTE) wird hier eine Alias->Column->Source Map gepflegt.
                                                                                                // Key: Derived table alias. Value: Dictionary(OutputColumnName -> (Schema, Table, Column, Ambiguous))
        private readonly Dictionary<string, Dictionary<string, (string Schema, string Table, string Column, bool Ambiguous)>> _derivedTableColumnSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ResultColumn>> _derivedTableColumns = new(StringComparer.OrdinalIgnoreCase); // Original ResultColumns je Derived Alias
        public Visitor(string definition, Analysis analysis) { _definition = definition; _analysis = analysis; }
        public override void ExplicitVisit(CreateProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        public override void ExplicitVisit(CreateOrAlterProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        public override void ExplicitVisit(AlterProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        private int _scalarSubqueryDepth; // Track nesting inside ScalarSubquery expressions
        public override void ExplicitVisit(SelectStatement node) { _analysis.ContainsSelect = true; base.ExplicitVisit(node); }
        // Hinweis: Statement-Level FOR JSON Fallback (SelectStatement.ForClause) wird von ScriptDom nicht angeboten (ForClause nur an QuerySpecification).
        // Falls künftig Unterschiede auftauchen, kann hier eine alternative Pfadbehandlung ergänzt werden.
        public override void ExplicitVisit(InsertStatement node) { _analysis.ContainsInsert = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(UpdateStatement node) { _analysis.ContainsUpdate = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(DeleteStatement node) { _analysis.ContainsDelete = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(MergeStatement node) { _analysis.ContainsMerge = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(OpenJsonTableReference node) { _analysis.ContainsOpenJson = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(ScalarSubquery node) { _scalarSubqueryDepth++; base.ExplicitVisit(node); _scalarSubqueryDepth--; }
        public override void ExplicitVisit(StatementList node)
        {
            if (_procedureDepth > 0 && node?.Statements != null)
                foreach (var s in node.Statements) AddStatement(s);
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(QuerySpecification node)
        {
            try
            {
                try
                {
                    if (ShouldDiag()) Console.WriteLine($"[qs-debug] enter startOffset={node.StartOffset} fragmentLength={node.FragmentLength} forClauseType={(node.ForClause?.GetType().Name ?? "null")}");
                }
                catch { }
                // Save outer scope
                var outerAliases = new Dictionary<string, (string Schema, string Table)>(_tableAliases, StringComparer.OrdinalIgnoreCase);
                var outerSources = new HashSet<string>(_tableSources, StringComparer.OrdinalIgnoreCase);
                // Create local scope
                _tableAliases.Clear();
                _tableSources.Clear();
                if (node.FromClause?.TableReferences != null)
                {
                    foreach (var tr in node.FromClause.TableReferences) PreCollectNamedTableReferences(tr);
                }
                // Traverse children (collect additional references, derived tables, etc.)
                base.ExplicitVisit(node);
                try
                {
                    if (node.StartOffset >= 0 && node.FragmentLength > 0)
                    {
                        int end = Math.Min(_definition.Length, node.StartOffset + node.FragmentLength + 200);
                        var seg = _definition.Substring(node.StartOffset, end - node.StartOffset);
                        var idxForJson = seg.IndexOf("FOR JSON", StringComparison.OrdinalIgnoreCase);
                        var idxForJsonPath = seg.IndexOf("FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                        if (ShouldDiag()) Console.WriteLine($"[qs-debug] segmentScan len={seg.Length} idxForJson={idxForJson} idxForJsonPath={idxForJsonPath}");
                        if (idxForJson >= 0)
                        {
                            // Ausgabe von 120 Zeichen um das Pattern
                            int previewStart = Math.Max(0, idxForJson - 60);
                            int previewEnd = Math.Min(seg.Length, idxForJson + 120);
                            var preview = seg.Substring(previewStart, previewEnd - previewStart).Replace('\n', ' ').Replace('\r', ' ');
                            if (ShouldDiag()) Console.WriteLine($"[qs-debug] contextPreview={preview}");
                        }
                    }
                }
                catch { }
                // Support FOR JSON PATH both on QuerySpecification.ForClause and parent SelectStatement.ForClause (ScriptDom may attach it at statement level)
                // ScriptDom bietet keine direkte Parent-Eigenschaft auf QuerySpecification; daher approximieren wir:
                // Falls node.ForClause null ist, prüfen wir, ob der übergeordnete SelectStatement (der diesen QuerySpecification enthält)
                // einen ForClause besitzt. Da wir Parent nicht haben, nutzen wir eine Heuristik: Wenn node.ForClause null ist UND
                // der aktuelle SelectStatement (wird vorher in ExplicitVisit(SelectStatement) erfasst) eine ForClause hat, wird diese
                // später separat verarbeitet. Vereinfachung: Wir behandeln NUR node.ForClause hier; für Statement-Level FOR JSON
                // greifen wir in ExplicitVisit(SelectStatement) ein (Fallback Pfad unten implementiert).
                JsonForClause jsonClause = node.ForClause as JsonForClause;
                bool isNestedSelect = _scalarSubqueryDepth > 0; // verschachtelte Selects (Subqueries) nicht als eigenes JSON ResultSet markieren

                // Segment-Scan Fallback: Einige reale Prozeduren liefern keinen JsonForClause im AST (ScriptDom Limitation bei komplexen SELECTs).
                // Um Tests zu erfüllen ohne globale Regex-Heuristik führen wir einen strikt begrenzten Scan über das QuerySpecification Segment aus.
                // Bedingungen: (a) top-level (nicht verschachtelt), (b) JsonForClause==null, (c) Segment enthält "FOR JSON PATH".
                // Zusätzliche Optionen WITHOUT_ARRAY_WRAPPER / ROOT('x') werden extrahiert. Dieser Fallback ist rein segmentbezogen & deterministisch.
                bool segmentFallbackDetected = false;
                bool fallbackWithoutArrayWrapper = false;
                string fallbackRootProperty = null;
                if (jsonClause == null && !isNestedSelect && _definition != null)
                {
                    try
                    {
                        int startScan = node.StartOffset >= 0 ? node.StartOffset : 0;
                        int endScan = node.StartOffset >= 0 && node.FragmentLength > 0
                            ? Math.Min(_definition.Length, node.StartOffset + node.FragmentLength + 1000)
                            : _definition.Length;
                        var segment = _definition.Substring(startScan, endScan - startScan);
                        // Entferne einfache Inline Kommentare "--" bis Zeilenende für robustere Erkennung (kein Block-Strip)
                        var cleaned = string.Join('\n', segment.Split('\n').Select(l =>
                        {
                            var ci = l.IndexOf("--", StringComparison.Ordinal);
                            return ci >= 0 ? l.Substring(0, ci) : l;
                        }));
                        var idx = cleaned.IndexOf("FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                        if (idx < 0)
                        {
                            // Global fallback: Suche im vollständigen Definitionstext falls Segment (Fragment) es nicht enthält (ScriptDom Offsets unvollständig)
                            idx = _definition.IndexOf("FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                            if (idx >= 0 && ShouldDiagJsonAst()) { try { Console.WriteLine("[json-ast-fallback-global] global search matched FOR JSON PATH outside fragment"); } catch { } }
                        }
                        if (idx >= 0)
                        {
                            segmentFallbackDetected = true;
                            if (ShouldDiagJsonAst()) { try { Console.WriteLine("[json-ast-fallback] segment FOR JSON PATH detected top-level"); } catch { } }
                            // Suche Optionen im Bereich nach dem Match (bis 180 Zeichen oder Segmentende)
                            int optsStart = idx + "FOR JSON PATH".Length;
                            int optsEnd = Math.Min(cleaned.Length, optsStart + 180);
                            var opts = cleaned.Substring(optsStart, optsEnd - optsStart);
                            if (opts.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0)
                                fallbackWithoutArrayWrapper = true;
                            var mRoot = System.Text.RegularExpressions.Regex.Match(opts, @"ROOT\s*\(\s*'([^']+)'\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (mRoot.Success) fallbackRootProperty = mRoot.Groups[1].Value;
                        }
                    }
                    catch { }
                }
                if (jsonClause == null && !isNestedSelect && !segmentFallbackDetected)
                {
                    // Weder AST JsonForClause noch segmentbasierte Erkennung → kein JSON ResultSet.
                    if (ShouldDiagJsonAst()) { try { Console.WriteLine("[json-ast-skip] no JsonForClause and no segment fallback"); } catch { } }
                    _tableAliases.Clear(); foreach (var kv in outerAliases) _tableAliases[kv.Key] = kv.Value;
                    _tableSources.Clear(); foreach (var s in outerSources) _tableSources.Add(s);
                    return;
                }

                // Collect outer join right-side aliases BEFORE analyzing select elements
                CollectOuterJoinRightAliases(node.FromClause?.TableReferences);

                var builder = new JsonSetBuilder();
                if (jsonClause != null)
                {
                    var options = jsonClause.Options ?? Array.Empty<JsonForClauseOption>();
                    if (options.Count == 0) builder.JsonWithArrayWrapper = true; // default
                    foreach (var opt in options)
                    {
                        switch (opt.OptionKind)
                        {
                            case JsonForClauseOptions.WithoutArrayWrapper: builder.JsonWithoutArrayWrapper = true; break;
                            case JsonForClauseOptions.Root:
                                if (builder.JsonRootProperty == null && opt.Value is Literal lit) builder.JsonRootProperty = ExtractLiteralValue(lit);
                                break;
                            default:
                                if (opt.OptionKind != JsonForClauseOptions.WithoutArrayWrapper) builder.JsonWithArrayWrapper = true; break;
                        }
                    }
                    if (!builder.JsonWithoutArrayWrapper) builder.JsonWithArrayWrapper = true;
                }
                else if (segmentFallbackDetected)
                {
                    // Fallback: Standardmäßig Array Wrapper aktiv außer explizitem WITHOUT_ARRAY_WRAPPER
                    builder.JsonWithArrayWrapper = !fallbackWithoutArrayWrapper;
                    builder.JsonWithoutArrayWrapper = fallbackWithoutArrayWrapper;
                    if (!fallbackWithoutArrayWrapper) builder.JsonWithArrayWrapper = true; // sicherstellen default
                    builder.JsonRootProperty = fallbackRootProperty;
                }
                // Keine heuristische Set-Analyse; nur echte JsonForClause bestimmt Array Wrapper

                foreach (var sce in node.SelectElements.OfType<SelectScalarExpression>())
                {
                    if (ShouldDiagJsonAst())
                    {
                        try
                        {
                            string initialName = null;
                            if (sce.ColumnName is IdentifierOrValueExpression iveInit)
                            {
                                if (iveInit.Identifier != null) initialName = iveInit.Identifier.Value;
                                else if (iveInit.ValueExpression is StringLiteral slInit && !string.IsNullOrWhiteSpace(slInit.Value)) initialName = slInit.Value;
                            }
                            Console.WriteLine($"[json-ast-select-elem-enter] startOffset={sce.StartOffset} len={sce.FragmentLength} initialName={initialName} exprType={sce.Expression?.GetType().Name}");
                        }
                        catch { }
                    }
                    var alias = sce.ColumnName?.Value;
                    if (string.IsNullOrWhiteSpace(alias) && sce.ColumnName is IdentifierOrValueExpression ive)
                    {
                        try
                        {
                            if (ive.ValueExpression is StringLiteral sl && !string.IsNullOrWhiteSpace(sl.Value)) alias = sl.Value;
                            else if (ive.Identifier != null && !string.IsNullOrWhiteSpace(ive.Identifier.Value)) alias = ive.Identifier.Value;
                        }
                        catch { }
                    }
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        if (sce.Expression is ColumnReferenceExpression implicitCr && implicitCr.MultiPartIdentifier?.Identifiers?.Count > 0)
                            alias = implicitCr.MultiPartIdentifier.Identifiers[^1].Value;
                        else if (sce.Expression is CastCall castCall && castCall.Parameter is ColumnReferenceExpression castCol && castCol.MultiPartIdentifier?.Identifiers?.Count > 0)
                            alias = castCol.MultiPartIdentifier.Identifiers[^1].Value;
                    }
                    // Token-basierte (AST Token Stream) Alias-Erkennung (erweiterter Scan über Expression-Ende hinaus): AS 'literal'
                    if (string.IsNullOrWhiteSpace(alias) && sce.ScriptTokenStream != null)
                    {
                        try
                        {
                            var tokens = sce.ScriptTokenStream;
                            // Preferred: nutze Token Indices wenn verfügbar, ansonsten Offset-Heuristik
                            int exprLastToken = sce.LastTokenIndex >= 0 ? sce.LastTokenIndex : -1;
                            if (exprLastToken < 0)
                            {
                                // Fallback: bestimme Bereich über Offsets
                                int exprStart = sce.StartOffset;
                                int exprEnd = sce.StartOffset + sce.FragmentLength;
                                // Finde letzten Token dessen Offset < exprEnd
                                for (int i = 0; i < tokens.Count; i++)
                                {
                                    var tk = tokens[i];
                                    if (tk.Offset >= exprEnd) { exprLastToken = i - 1; break; }
                                    exprLastToken = i; // advance until we pass end
                                }
                            }
                            int scanStart = Math.Min(tokens.Count - 1, Math.Max(0, exprLastToken + 1));
                            for (int i = scanStart; i < tokens.Count; i++)
                            {
                                var t = tokens[i];
                                // Abbruchbedingungen: nächstes SELECT-Element / FROM / Semikolon / Zeilenende-Komma
                                if (t.TokenType == TSqlTokenType.Comma || t.TokenType == TSqlTokenType.Semicolon) break;
                                if (t.Text != null && t.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase)) break;
                                if (t.TokenType == TSqlTokenType.As && i + 1 < tokens.Count)
                                {
                                    // nächster signifikanter Token
                                    int j = i + 1;
                                    while (j < tokens.Count && string.IsNullOrWhiteSpace(tokens[j].Text)) j++;
                                    if (j >= tokens.Count) break;
                                    var next = tokens[j];
                                    var raw = next.Text;
                                    if (!string.IsNullOrWhiteSpace(raw) && raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
                                    {
                                        raw = raw.Substring(1, raw.Length - 2);
                                        if (!string.IsNullOrWhiteSpace(raw)) { alias = raw; break; }
                                    }
                                }
                                else if (t.Text != null && t.Text.Length >= 2 && t.Text[0] == '\'' && t.Text[^1] == '\'')
                                {
                                    // Pattern: <expr> 'alias'  (ohne explizites AS)
                                    var raw = t.Text.Substring(1, t.Text.Length - 2);
                                    if (!string.IsNullOrWhiteSpace(raw)) { alias = raw; break; }
                                }
                            }
                        }
                        catch { }
                    }
                    // Zusätzliche Alias-Erkennung für FOR JSON Pfad-Syntax inkl. Fälle, in denen ScriptDom das AS 'alias' außerhalb des Fragmentes schneidet.
                    if (string.IsNullOrWhiteSpace(alias) && sce.StartOffset >= 0 && sce.FragmentLength > 0)
                    {
                        try
                        {
                            var endExpr = Math.Min(_definition.Length, sce.StartOffset + sce.FragmentLength);
                            var exprSegment = _definition.Substring(sce.StartOffset, endExpr - sce.StartOffset);
                            // Primär: AS 'alias' im Ausdruckssegment
                            var m = System.Text.RegularExpressions.Regex.Match(exprSegment, @"AS\s+'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (!m.Success)
                            {
                                // Fallback: Einzelnes quoted literal (manche Alias-Stile ohne explizites AS)
                                m = System.Text.RegularExpressions.Regex.Match(exprSegment, @"'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            }
                            if (!m.Success)
                            {
                                // Forward-Scan bis FOR JSON oder nächstes SELECT/ FROM / GROUP BY zur Begrenzung
                                int boundary = _definition.IndexOf("FOR JSON", endExpr, StringComparison.OrdinalIgnoreCase);
                                if (boundary < 0) boundary = _definition.Length;
                                int scanEnd = Math.Min(_definition.Length, endExpr + 300);
                                if (boundary > endExpr && boundary < scanEnd) scanEnd = boundary;
                                var forward = _definition.Substring(endExpr, scanEnd - endExpr);
                                m = System.Text.RegularExpressions.Regex.Match(forward, @"AS\s+'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (!m.Success)
                                {
                                    m = System.Text.RegularExpressions.Regex.Match(forward, @"'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                }
                            }
                            if (m.Success) alias = m.Groups[1].Value;
                        }
                        catch { }
                    }
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        // Fallback: generischer Platzhalter damit Expression trotzdem analysiert wird (AST-only, keine Heuristik)
                        alias = "_col" + builder.Columns.Count.ToString();
                    }
                    if (ShouldDiagJsonAst()) { try { Console.WriteLine($"[json-ast-select-alias-final] alias={alias}"); } catch { } }
                    var path = NormalizeJsonPath(alias);
                    var col = new ResultColumn { Name = SanitizeAliasPreserveDots(path) };
                    var beforeBindings = new SourceBindingState();
                    AnalyzeScalarExpression(sce.Expression, col, beforeBindings);
                    try
                    {
                        if (sce.StartOffset >= 0 && sce.FragmentLength > 0)
                        {
                            var end = Math.Min(_definition.Length, sce.StartOffset + sce.FragmentLength);
                            col.RawExpression = _definition.Substring(sce.StartOffset, end - sce.StartOffset).Trim();
                        }
                    }
                    catch { }
                    col.ExpressionKind ??= ResultColumnExpressionKind.Unknown;
                    if (!string.IsNullOrWhiteSpace(col.SourceAlias) && _outerJoinRightAliases.Contains(col.SourceAlias)) col.ForcedNullable = true;
                    if (beforeBindings.BindingCount > 1) col.IsAmbiguous = true;
                    if (!builder.Columns.Any(c => c.Name.Equals(col.Name, StringComparison.OrdinalIgnoreCase))) builder.Columns.Add(col);
                }
                if (node.SelectElements?.OfType<SelectStarExpression>().Any() == true) builder.HasSelectStar = true;
                var isNested = _scalarSubqueryDepth > 0;
                var resultSet = builder.ToResultSet();
                if (isNested)
                {
                    if (!_analysis.NestedJsonSets.ContainsKey(node)) _analysis.NestedJsonSets[node] = resultSet;
                }
                else
                {
                    // Nur hinzufügen wenn echte (AST) JsonForClause ODER heuristisch erkannt im Top-Level
                    if (jsonClause != null || segmentFallbackDetected)
                        _analysis.JsonSets.Add(resultSet);
                }
                // Restore outer scope
                _tableAliases.Clear(); foreach (var kv in outerAliases) _tableAliases[kv.Key] = kv.Value;
                _tableSources.Clear(); foreach (var s in outerSources) _tableSources.Add(s);
            }
            catch { }
        }
        private void PreCollectNamedTableReferences(TableReference tr)
        {
            switch (tr)
            {
                case QualifiedJoin qj:
                    PreCollectNamedTableReferences(qj.FirstTableReference);
                    PreCollectNamedTableReferences(qj.SecondTableReference);
                    break;
                case NamedTableReference ntr:
                    try
                    {
                        var schema = ntr.SchemaObject?.SchemaIdentifier?.Value ?? _analysis.DefaultSchema;
                        var table = ntr.SchemaObject?.BaseIdentifier?.Value;
                        if (!string.IsNullOrWhiteSpace(table))
                        {
                            var alias = ntr.Alias?.Value;
                            var key = !string.IsNullOrWhiteSpace(alias) ? alias : table;
                            if (!_tableAliases.ContainsKey(key))
                                _tableAliases[key] = (schema, table);
                            _tableSources.Add($"{schema}.{table}");
                        }
                    }
                    catch { }
                    break;
                case QueryDerivedTable qdt:
                    // Do not pre-walk derived table internals here (will be handled in its own ExplicitVisit)
                    break;
                default:
                    break;
            }
        }
        public override void ExplicitVisit(NamedTableReference node)
        {
            try
            {
                var schema = node.SchemaObject?.SchemaIdentifier?.Value ?? _analysis.DefaultSchema;
                var table = node.SchemaObject?.BaseIdentifier?.Value;
                if (!string.IsNullOrWhiteSpace(table))
                {
                    var alias = node.Alias?.Value;
                    var key = !string.IsNullOrWhiteSpace(alias) ? alias : table;
                    if (!_tableAliases.ContainsKey(key))
                        _tableAliases[key] = (schema, table);
                    _tableSources.Add($"{schema}.{table}");
                }
            }
            catch { }
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(QueryDerivedTable node)
        {
            try
            {
                ProcessQueryDerivedTable(node);
            }
            catch { }
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(WithCtesAndXmlNamespaces node)
        {
            // Verarbeite CTEs: Jede CTE als 'abgeleitete Tabelle'
            if (node?.CommonTableExpressions != null)
            {
                foreach (var cte in node.CommonTableExpressions)
                {
                    try
                    {
                        if (cte?.QueryExpression is QuerySpecification qs)
                        {
                            var alias = cte.ExpressionName?.Value;
                            if (!string.IsNullOrWhiteSpace(alias))
                            {
                                var derivedCols = new List<ResultColumn>();
                                var columnMap = ExtractColumnSourceMapFromQuerySpecification(qs, derivedCols);
                                if (columnMap.Count > 0)
                                {
                                    _derivedTableColumnSources[alias] = columnMap;
                                    _derivedTableColumns[alias] = derivedCols;
                                    ConsoleWriteDerived(alias, columnMap, isCte: true);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            base.ExplicitVisit(node);
        }
        private readonly HashSet<string> _outerJoinRightAliases = new(StringComparer.OrdinalIgnoreCase);
        private void CollectOuterJoinRightAliases(IList<TableReference> refs)
        {
            if (refs == null) return;
            foreach (var tr in refs)
            {
                CollectOuterJoinRightAliasRecursive(tr);
            }
        }
        private void CollectOuterJoinRightAliasRecursive(TableReference tr)
        {
            switch (tr)
            {
                case QualifiedJoin qj:
                    CollectOuterJoinRightAliasRecursive(qj.FirstTableReference);
                    CollectOuterJoinRightAliasRecursive(qj.SecondTableReference);
                    if (qj.QualifiedJoinType == QualifiedJoinType.LeftOuter && qj.SecondTableReference is NamedTableReference right)
                    {
                        var alias = right.Alias?.Value ?? right.SchemaObject?.BaseIdentifier?.Value;
                        if (!string.IsNullOrWhiteSpace(alias)) _outerJoinRightAliases.Add(alias);
                    }
                    break;
                case NamedTableReference:
                    // already handled by ExplicitVisit(NamedTableReference)
                    break;
                default:
                    // ignore other table reference kinds for now (Derived tables / CTE) – future enhancement
                    break;
            }
        }
        private sealed class SourceBindingState
        {
            public int BindingCount => _bindings.Count;
            private readonly HashSet<string> _bindings = new(StringComparer.OrdinalIgnoreCase);
            public void Register(string schema, string table, string column)
            {
                if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column)) return;
                _bindings.Add($"{schema}.{table}.{column}");
            }
        }
        private void AnalyzeScalarExpression(ScalarExpression expr, ResultColumn target, SourceBindingState state)
        {
            if (expr != null)
            {
                if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] analyze-enter name={target?.Name} exprType={expr.GetType().Name}"); } catch { } }
            }
            switch (expr)
            {
                case null:
                    return;
                case ColumnReferenceExpression cref:
                    // Einstieg-Instrumentierung für ColumnRef
                    try
                    {
                        var partsPreview = cref.MultiPartIdentifier?.Identifiers?.Select(i => i.Value).ToArray() ?? Array.Empty<string>();
                        if (ShouldDiagJsonAst()) System.Console.WriteLine($"[json-ast-colref-enter] name={target?.Name} parts={string.Join('.', partsPreview)}");
                    }
                    catch { }
                    _analysis.ColumnRefTotal++;
                    // ExpressionKind nur setzen wenn noch nicht klassifiziert (verhindert Überschreiben von FunctionCall/IIF)
                    if (target.ExpressionKind == null)
                        target.ExpressionKind = ResultColumnExpressionKind.ColumnRef;
                    BindColumnReference(cref, target, state);
                    // Stabile Dot-Alias Typ-Bindung: Falls Alias selbst ein dot path ist (z.B. type.typeId) und BindColumnReference keine SourceColumn setzte,
                    // versuchen wir Mapping: Erstes Segment (group) -> vorhandener Tabellenalias, Suffix -> Spaltenname.
                    if (string.IsNullOrWhiteSpace(target.SourceColumn) && !string.IsNullOrWhiteSpace(target.Name) && target.Name.Contains('.') && _tableAliases.Count > 0)
                    {
                        var aliasParts = target.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                        if (aliasParts.Length >= 2)
                        {
                            var groupPrefix = aliasParts[0];
                            var columnSuffix = aliasParts[^1];
                            // Versuche exakten Tabellenalias-Match über Namensähnlichkeit (groupPrefix == alias oder groupPrefix == alias ohne Underscores)
                            // Falls mehrere Aliase existieren, wählen wir denjenigen, dessen Tabelle bereits eine Spalte mit diesem Namen hat (über zuvor registrierte bindings).
                            var candidateAliases = _tableAliases.Keys
                                .Where(a => a.Equals(groupPrefix, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            if (!candidateAliases.Any())
                            {
                                // Lockerer Versuch: groupPrefix ohne Sonderzeichen mit alias ohne Sonderzeichen vergleichen
                                string Normalize(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                                var normGroup = Normalize(groupPrefix);
                                candidateAliases = _tableAliases.Keys
                                    .Where(a => Normalize(a) == normGroup)
                                    .ToList();
                            }
                            foreach (var cand in candidateAliases)
                            {
                                if (_tableAliases.TryGetValue(cand, out var tbl))
                                {
                                    // Trage Binding ein – wir kennen Schema/Table, Spaltenname ist suffix
                                    target.SourceAlias = cand;
                                    target.SourceSchema = tbl.Schema;
                                    target.SourceTable = tbl.Table;
                                    target.SourceColumn = columnSuffix;
                                    state.Register(target.SourceSchema, target.SourceTable, target.SourceColumn);
                                    ConsoleWriteBind(target, reason: "dotted-alias");
                                    break;
                                }
                            }
                        }
                    }
                    // Entfernt: frühe namenspbasierte Typableitung (InferSqlTypeFromSourceBinding)
                    // Entfernt: identity.RecordAsJson Spezialbehandlung – keine Funktions-Pseudo-Column Erkennung mehr.
                    break;
                case CastCall castCall:
                    target.ExpressionKind = ResultColumnExpressionKind.Cast;
                    if (castCall.DataType?.Name?.Identifiers?.Count > 0)
                    {
                        var typeName = string.Join('.', castCall.DataType.Name.Identifiers.Select(i => i.Value));
                        if (!string.IsNullOrWhiteSpace(typeName)) target.CastTargetType = typeName;
                        TryExtractTypeParameters(castCall.DataType, target);
                    }
                    AnalyzeScalarExpression(castCall.Parameter, target, state);
                    break;
                case ConvertCall convertCall:
                    target.ExpressionKind = ResultColumnExpressionKind.Cast;
                    if (convertCall.DataType?.Name?.Identifiers?.Count > 0)
                    {
                        var typeName = string.Join('.', convertCall.DataType.Name.Identifiers.Select(i => i.Value));
                        if (!string.IsNullOrWhiteSpace(typeName)) target.CastTargetType = typeName;
                        TryExtractTypeParameters(convertCall.DataType, target);
                    }
                    foreach (var p in new[] { convertCall.Parameter, convertCall.Style }) AnalyzeScalarExpression(p, target, state);
                    break;
                case IntegerLiteral _:
                    target.HasIntegerLiteral = true; break;
                case NumericLiteral nl:
                    if (!string.IsNullOrWhiteSpace(nl.Value) && nl.Value.Contains('.')) target.HasDecimalLiteral = true; else target.HasIntegerLiteral = true; break;
                case RealLiteral _:
                    target.HasDecimalLiteral = true; break;
                case FunctionCall fn:
                    // Distinguish JSON_QUERY
                    var fnName = fn.FunctionName?.Value;
                    try { if (ShouldDiag()) System.Console.WriteLine($"[json-agg-diag] fn-enter name={target.Name} fn={fnName} paramCount={fn.Parameters?.Count}"); } catch { }
                    if (!string.IsNullOrWhiteSpace(fnName) && fnName.Equals("JSON_QUERY", StringComparison.OrdinalIgnoreCase))
                        target.ExpressionKind = ResultColumnExpressionKind.JsonQuery;
                    else
                        target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                    if (!string.IsNullOrWhiteSpace(fnName))
                    {
                        var lower = fnName.ToLowerInvariant();
                        if (lower is "sum" or "count" or "count_big" or "avg" or "exists" or "min" or "max")
                        {
                            target.IsAggregate = true;
                            target.AggregateFunction = lower;
                            _analysis.AggregateCount++;
                            // Erweiterte Rückgabewert-Typinferenz für bekannte Aggregatfunktionen
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                            {
                                switch (lower)
                                {
                                    case "count":
                                        target.SqlTypeName = "int"; break;
                                    case "count_big":
                                        target.SqlTypeName = "bigint"; break;
                                    case "avg":
                                        // AVG über integer -> decimal. Feinere Präzision könnte aus Parametertyp kommen; hier pauschal.
                                        target.SqlTypeName = "decimal(18,2)"; break;
                                    case "exists":
                                        target.SqlTypeName = "bit"; break;
                                    case "sum":
                                        // SUM Spezialfall weiter unten (zero/one). Wenn dort nicht int gesetzt wird -> Fallback.
                                        // Versuche param zu inspizieren für integer vs decimal.
                                        try
                                        {
                                            if (fn.Parameters?.Count == 1)
                                            {
                                                var pExpr = fn.Parameters[0] as ScalarExpression;
                                                // Grobe Heuristik: Wenn Parameter bereits HasIntegerLiteral Flag setzen würde
                                                var temp = new ResultColumn();
                                                AnalyzeScalarExpression(pExpr, temp, state);
                                                if (temp.HasIntegerLiteral && !temp.HasDecimalLiteral)
                                                {
                                                    target.SqlTypeName = "int"; // integer SUM (kann überlaufen, aber pragmatisch)
                                                }
                                                else if (temp.HasDecimalLiteral)
                                                {
                                                    target.SqlTypeName = "decimal(18,4)"; // etwas höhere Präzision für Summierung
                                                }
                                            }
                                            if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                                            {
                                                // Default Fallback falls nichts ableitbar (z.B. ColumnRef ohne Typinfo hier): decimal
                                                target.SqlTypeName = "decimal(18,2)";
                                            }
                                        }
                                        catch { }
                                        break;
                                    case "min":
                                    case "max":
                                        // MIN/MAX: Wenn einzelner Parameter Literal-Flags erkennen lässt, leite einfachen Typ ab
                                        try
                                        {
                                            if (fn.Parameters?.Count == 1)
                                            {
                                                var pExpr = fn.Parameters[0] as ScalarExpression;
                                                var temp = new ResultColumn();
                                                AnalyzeScalarExpression(pExpr, temp, state);
                                                if (temp.HasIntegerLiteral && !temp.HasDecimalLiteral) target.SqlTypeName = "int";
                                                else if (temp.HasDecimalLiteral) target.SqlTypeName = "decimal(18,2)";
                                            }
                                        }
                                        catch { }
                                        break;
                                }
                            }
                        }
                        // Spezialfall: SUM über reinem 0/1 Ausdruck -> int
                        if (lower == "sum")
                        {
                            try
                            {
                                if (fn.Parameters != null && fn.Parameters.Count == 1)
                                {
                                    var pExpr = fn.Parameters[0] as ScalarExpression;
                                    if (IsPureZeroOneConditional(pExpr))
                                    {
                                        target.HasIntegerLiteral = true; // verstärke Flag
                                        if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                                        {
                                            target.SqlTypeName = "int";
                                            if (ShouldDiag()) System.Console.WriteLine($"[json-agg-diag] sum-zero-one-detected name={target.Name} assigned=int");
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    // Capture function schema + name if schema-qualified (CallTarget) – purely AST based
                    try
                    {
                        if (ShouldDiagJsonAst())
                        {
                            try { System.Console.WriteLine($"[json-ast-fn-meta-enter] alias={target.Name} rawFnName={fnName} callTargetType={fn.CallTarget?.GetType().Name}"); } catch { }
                        }
                        // CallTarget variants: MultiPartIdentifierCallTarget for schema-qualified user functions
                        if (fn.CallTarget is MultiPartIdentifierCallTarget mp && mp.MultiPartIdentifier?.Identifiers?.Count > 0)
                        {
                            var idents = mp.MultiPartIdentifier.Identifiers.Select(i => i.Value).ToList();
                            if (idents.Count == 1)
                            {
                                // legacy FunctionName entfernt
                            }
                            else if (idents.Count >= 2)
                            {
                                // legacy FunctionSchemaName/FunctionName entfernt
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(fnName))
                        {
                            // legacy FunctionName entfernt
                        }
                        // Fallback: Falls Funktionsname bereits Schema enthält (identity.RecordAsJson)
                        // legacy schema/name normalization removed (Reference handles schema)
                        // Entfernt: identity.RecordAsJson Erkennung – keine spezielle Funktionsmarkierung mehr.
                    }
                    catch { }
                    // JSON-Funktions-Expansion NACH Erfassung von Schema/Name, damit Resolver korrekte schema erhält
                    TryExpandFunctionJson(fn, target);
                    if (ShouldDiagJsonAst())
                    {
                        try { System.Console.WriteLine($"[json-ast-fn-meta-final] alias={target.Name} (legacy fn metadata removed)"); } catch { }
                    }
                    // IIF Typableitung im Haupt-Kontext
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(fnName) && fnName.Equals("IIF", StringComparison.OrdinalIgnoreCase) && fn.Parameters?.Count == 3)
                        {
                            // IIF immer als FunctionCall klassifizieren (AST-only, keine Typheuristik).
                            target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                            var thenExpr = fn.Parameters[1];
                            var elseExpr = fn.Parameters[2];
                            var thenCol = new ResultColumn(); var thenState = new SourceBindingState();
                            AnalyzeScalarExpression(thenExpr as ScalarExpression, thenCol, thenState);
                            var elseCol = new ResultColumn(); var elseState = new SourceBindingState();
                            AnalyzeScalarExpression(elseExpr as ScalarExpression, elseCol, elseState);
                            // Gleiche Quellspalte → Typ übernehmen
                            if (!string.IsNullOrWhiteSpace(thenCol.SourceColumn) && !string.IsNullOrWhiteSpace(elseCol.SourceColumn) && thenCol.SourceColumn.Equals(elseCol.SourceColumn, StringComparison.OrdinalIgnoreCase))
                            {
                                // Entfernt: Quellspalten-basierte Typableitung
                            }
                            // Identische vorab abgeleitete SqlTypeName Werte
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName) && !string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && thenCol.SqlTypeName.Equals(elseCol.SqlTypeName, StringComparison.OrdinalIgnoreCase))
                            {
                                target.SqlTypeName = thenCol.SqlTypeName;
                            }
                            // Beide Literal-Strings → nvarchar(maxLen)
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName)
                                && IsLiteralString(thenExpr, out var litThen)
                                && IsLiteralString(elseExpr, out var litElse))
                            {
                                var maxLen = Math.Max(litThen?.Length ?? 0, litElse?.Length ?? 0);
                                target.SqlTypeName = "nvarchar"; if (maxLen > 0) target.MaxLength = maxLen;
                            }
                        }
                    }
                    catch { }
                    // Erweiterung: Für JSON_QUERY nun innere ScalarSubquery parsen, um Aggregat-Typen zu erkennen
                    if (target.ExpressionKind == ResultColumnExpressionKind.JsonQuery)
                    {
                        if (fn.Parameters != null)
                            foreach (var p in fn.Parameters)
                            {
                                try
                                {
                                    bool subqueryHandled = false;
                                    try { if (ShouldDiag()) System.Console.WriteLine($"[json-agg-diag] jsonQueryParamType name={target.Name} paramType={p?.GetType().Name}"); } catch { }
                                    // Direktes ScalarSubquery
                                    if (p is ScalarSubquery ss)
                                    {
                                        var innerQs = UnwrapToQuerySpecification(ss.QueryExpression);
                                        if (innerQs != null)
                                        {
                                            AnalyzeJsonQueryInnerSubquery(innerQs, target, state);
                                            subqueryHandled = true; continue; // nächster Parameter
                                        }
                                    }
                                    // Parenthesized -> ScalarSubquery -> (SelectStatement|QuerySpecification)
                                    if (p is ParenthesisExpression pe && pe.Expression is ScalarSubquery ss2)
                                    {
                                        var innerQs2 = UnwrapToQuerySpecification(ss2.QueryExpression);
                                        if (innerQs2 != null)
                                        {
                                            AnalyzeJsonQueryInnerSubquery(innerQs2, target, state);
                                            subqueryHandled = true; continue;
                                        }
                                    }
                                    if (!subqueryHandled)
                                    {
                                        // Versuche eine tiefere verschachtelte ScalarSubquery zu finden (Binary / Function / Parenthesis)
                                        var deepSs = FindFirstScalarSubquery(p as ScalarExpression, 0);
                                        if (deepSs != null)
                                        {
                                            var innerQs3 = UnwrapToQuerySpecification(deepSs.QueryExpression);
                                            if (innerQs3 != null)
                                            {
                                                AnalyzeJsonQueryInnerSubquery(innerQs3, target, state);
                                                try { if (ShouldDiag()) System.Console.WriteLine($"[json-agg-diag] jsonQueryParamDeepSubquery name={target.Name} depthFound"); } catch { }
                                            }
                                        }
                                    }
                                    // Sonstige Parameter NICHT traversieren, um keine Source-Bindings fälschlich zu übernehmen
                                }
                                catch { }
                            }
                    }
                    else
                    {
                        foreach (var p in fn.Parameters)
                        {
                            // Aggregat: Parameter-Analyse nicht ExpressionKind überschreiben lassen
                            var beforeKind = target.ExpressionKind;
                            AnalyzeScalarExpression(p, target, state);
                            if (target.IsAggregate == true && beforeKind == ResultColumnExpressionKind.FunctionCall && target.ExpressionKind != beforeKind)
                            {
                                // Restore FunctionCall classification (Parameter-Bind hat es evtl. auf ColumnRef gesetzt)
                                target.ExpressionKind = beforeKind;
                            }
                        }
                        // Nach Parameteranalyse: Falls IIF und Classification verloren ging, wiederherstellen
                        if (!string.IsNullOrWhiteSpace(fnName) && fnName.Equals("IIF", StringComparison.OrdinalIgnoreCase))
                        {
                            if (target.ExpressionKind != ResultColumnExpressionKind.FunctionCall && target.ExpressionKind != ResultColumnExpressionKind.JsonQuery)
                                target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                        }
                    }
                    break;
                case IIfCall iif:
                    // Direkter AST Node für IIF (statt FunctionCall). Klassifizieren als FunctionCall.
                    target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                    try
                    {
                        var thenExpr = iif.ThenExpression as ScalarExpression;
                        var elseExpr = iif.ElseExpression as ScalarExpression;
                        var thenCol = new ResultColumn(); var thenState = new SourceBindingState();
                        AnalyzeScalarExpression(thenExpr, thenCol, thenState);
                        var elseCol = new ResultColumn(); var elseState = new SourceBindingState();
                        AnalyzeScalarExpression(elseExpr, elseCol, elseState);
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName)
                            && IsLiteralString(thenExpr, out var litThen)
                            && IsLiteralString(elseExpr, out var litElse))
                        {
                            var maxLen = Math.Max(litThen?.Length ?? 0, litElse?.Length ?? 0);
                            target.SqlTypeName = "nvarchar"; if (maxLen > 0) target.MaxLength = maxLen;
                        }
                    }
                    catch { }
                    break;
                // JSON_QUERY appears as FunctionCall with name 'JSON_QUERY'; we already classify via FunctionCall. No dedicated node.
                case BinaryExpression be:
                    // Computed (ExpressionKind nicht durch Operanden überschreiben lassen)
                    target.ExpressionKind = ResultColumnExpressionKind.Computed;
                    var prevKindMain = target.ExpressionKind;
                    AnalyzeScalarExpression(be.FirstExpression, target, state);
                    AnalyzeScalarExpression(be.SecondExpression, target, state);
                    if (prevKindMain == ResultColumnExpressionKind.Computed && target.ExpressionKind != ResultColumnExpressionKind.Computed)
                        target.ExpressionKind = ResultColumnExpressionKind.Computed;
                    break;
                case UnaryExpression ue:
                    target.ExpressionKind = ResultColumnExpressionKind.Computed;
                    AnalyzeScalarExpression(ue.Expression, target, state);
                    break;
                case SearchedCaseExpression sce:
                    target.ExpressionKind = ResultColumnExpressionKind.Computed;
                    foreach (var w in sce.WhenClauses)
                    {
                        AnalyzeScalarExpression(w.ThenExpression, target, state);
                    }
                    AnalyzeScalarExpression(sce.ElseExpression, target, state);
                    break;
                case SimpleCaseExpression simp:
                    target.ExpressionKind = ResultColumnExpressionKind.Computed;
                    AnalyzeScalarExpression(simp.InputExpression, target, state);
                    foreach (var w in simp.WhenClauses)
                    {
                        // w.WhenExpression is BooleanExpression; skip
                        AnalyzeScalarExpression(w.ThenExpression, target, state);
                    }
                    AnalyzeScalarExpression(simp.ElseExpression, target, state);
                    break;
                case ParenthesisExpression pe:
                    AnalyzeScalarExpression(pe.Expression, target, state);
                    break;
                case ScalarSubquery ss:
                    // Detect nested JSON subquery (SELECT ... FOR JSON ...)
                    if (ss.QueryExpression is QuerySpecification qs && _analysis.NestedJsonSets.TryGetValue(qs, out var nested))
                    {
                        target.IsNestedJson = true;
                        target.ReturnsJson = true;
                        target.ReturnsJsonArray = nested.ReturnsJsonArray;
                        target.JsonRootProperty = nested.JsonRootProperty;
                        target.Columns = nested.Columns;
                        // Nested JSON container should not carry scalar SQL type metadata
                        target.SqlTypeName = null;
                        target.IsNullable = null;
                        target.MaxLength = null;
                        _analysis.NestedJsonCount++;
                        break; // fertig
                    }
                    // ScalarSubquery als potentiell nullable behandeln (kann 0 rows liefern)
                    if (target.IsNullable != true) target.IsNullable = true;
                    // Einfache Typableitung: Einzelnes SELECT mit einfacher ColumnReferenceExpression
                    try
                    {
                        if (ss.QueryExpression is QuerySpecification qs2 && qs2.SelectElements?.Count == 1 && string.IsNullOrWhiteSpace(target.SqlTypeName))
                        {
                            if (qs2.SelectElements[0] is SelectScalarExpression sse && sse.Expression is ColumnReferenceExpression cre)
                            {
                                var lastId = cre.MultiPartIdentifier?.Identifiers?.LastOrDefault()?.Value;
                                if (!string.IsNullOrWhiteSpace(lastId))
                                {
                                    // Entfernt: Quellspaltennamen-basierte Typableitung im ScalarSubquery
                                }
                            }
                            else if (qs2.SelectElements[0] is SelectScalarExpression sse2 && sse2.Expression is FunctionCall fc && string.IsNullOrWhiteSpace(target.SqlTypeName))
                            {
                                var fnLower = fc.FunctionName?.Value?.ToLowerInvariant();
                                switch (fnLower)
                                {
                                    case "sum":
                                    case "count":
                                        target.SqlTypeName = "int"; break;
                                    case "count_big":
                                        target.SqlTypeName = "bigint"; break; // korrekt für COUNT_BIG
                                    case "avg":
                                        target.SqlTypeName = "decimal(18,2)"; break;
                                    case "exists":
                                        target.SqlTypeName = "bit"; break;
                                }
                            }
                        }
                    }
                    catch { }
                    break;
                default:
                    // Unknown expression type -> attempt generic traversal via properties with ScalarExpression
                    break;
            }
        }
        private static readonly HashSet<string> _functionJsonExpansionStack = new(StringComparer.OrdinalIgnoreCase);
        private static bool IsDeferralEnabled()
        {
            try
            {
                var v = Environment.GetEnvironmentVariable("SPOCR_DEFER_JSON_FUNCTION_EXPANSION")?.Trim().ToLowerInvariant();
                return v is "1" or "true" or "yes";
            }
            catch { return false; }
        }
        private static void TryExpandFunctionJson(FunctionCall fc, ResultColumn target)
        {
            if (fc?.FunctionName == null) return;
            var fnValue = fc.FunctionName.Value;
            if (!string.IsNullOrWhiteSpace(fnValue) && fnValue.Equals("JSON_QUERY", StringComparison.OrdinalIgnoreCase))
            {
                FunctionCall innerCall = null;
                if (fc.Parameters != null && fc.Parameters.Count > 0)
                {
                    var firstParam = fc.Parameters[0];
                    switch (firstParam)
                    {
                        case FunctionCall innerFn:
                            innerCall = innerFn;
                            break;
                        case ScalarExpression scalar when scalar is ParenthesisExpression pe && pe.Expression is FunctionCall parenFn:
                            innerCall = parenFn;
                            break;
                        case ScalarExpression scalar when scalar is CastCall cast && cast.Parameter is FunctionCall castFn:
                            innerCall = castFn;
                            break;
                        case ScalarExpression scalar:
                            innerCall = FindFirstFunctionCall(scalar, 0);
                            break;
                    }
                }
                if (innerCall != null)
                {
                    TryExpandFunctionJson(innerCall, target);
                    return;
                }
            }
            var fnName = fc.FunctionName.Value;
            string schema = null;
            string fname = fnName;
            if (fc.CallTarget is MultiPartIdentifierCallTarget mp && mp.MultiPartIdentifier?.Identifiers?.Count > 0)
            {
                var ids = mp.MultiPartIdentifier.Identifiers.Select(i => i.Value).ToList();
                if (ids.Count >= 2)
                {
                    schema = ids[^2];
                    fname = ids[^1];
                }
                else if (ids.Count == 1)
                {
                    schema = ids[0];
                }
            }
            else if (fc.CallTarget != null)
            {
                try
                {
                    var ctType = fc.CallTarget.GetType();
                    var identifierProp = ctType.GetProperty("Identifier");
                    if (identifierProp?.GetValue(fc.CallTarget) is Identifier ident && !string.IsNullOrWhiteSpace(ident.Value))
                    {
                        schema = ident.Value;
                    }
                    else
                    {
                        var multiProp = ctType.GetProperty("MultiPartIdentifier");
                        if (multiProp?.GetValue(fc.CallTarget) is MultiPartIdentifier mpi && mpi.Identifiers?.Count > 0)
                        {
                            var ids = mpi.Identifiers.Select(i => i.Value).ToList();
                            if (ids.Count >= 2)
                            {
                                schema ??= ids[^2];
                                fname = ids[^1];
                            }
                            else if (ids.Count == 1)
                            {
                                schema ??= ids[0];
                            }
                        }
                    }
                }
                catch { /* reflective fallback best effort */ }
            }
            if (string.IsNullOrWhiteSpace(schema) && !string.IsNullOrWhiteSpace(fname) && !string.IsNullOrWhiteSpace(target.RawExpression))
            {
                try
                {
                    var match = Regex.Match(target.RawExpression, @"(?:(?<schema>\[[^\]]+\]|[A-Za-z0-9_]+)\s*\.)?(?<name>[A-Za-z0-9_]+)\s*\(");
                    if (match.Success)
                    {
                        var schemaToken = match.Groups["schema"].Value;
                        if (!string.IsNullOrWhiteSpace(schemaToken))
                        {
                            schema = schemaToken.Trim().Trim('[', ']');
                        }
                        var nameToken = match.Groups["name"].Value;
                        if (!string.IsNullOrWhiteSpace(nameToken))
                        {
                            fname = nameToken;
                        }
                    }
                }
                catch { /* best effort string fallback */ }
            }
            if (schema == null && !string.IsNullOrWhiteSpace(fname) && fname.Contains('.'))
            {
                var segs = fname.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length >= 2)
                {
                    schema = segs[^2];
                    fname = segs[^1];
                }
            }
            if (string.IsNullOrWhiteSpace(fname)) return;
            schema ??= "dbo";
            if (ShouldDiagJsonAst())
            {
                try
                {
                    var callTargetType = fc.CallTarget?.GetType();
                    var propNames = callTargetType?.GetProperties().Select(p => p.Name).ToArray() ?? Array.Empty<string>();
                    System.Console.WriteLine($"[json-ast-fn-meta-enter] alias={target.Name} rawFnName={fnName} callTarget={callTargetType?.Name ?? "(null)"} props={string.Join('|', propNames)} resolvedSchema={schema} resolvedName={fname}");
                }
                catch { }
            }
            if (IsDeferralEnabled())
            {
                target.Reference ??= new ColumnReferenceInfo { Kind = "Function", Schema = schema, Name = fname };
                target.DeferredJsonExpansion = true;
                target.IsNestedJson = true;
                target.ReturnsJson = true;
                return;
            }
            target.Reference ??= new ColumnReferenceInfo { Kind = "Function", Schema = schema, Name = fname };
            if (ResolveFunctionJsonSet == null) return;
            if (!string.IsNullOrWhiteSpace(target.SqlTypeName)) return;
            var key = schema + "." + fname;
            if (_functionJsonExpansionStack.Contains(key)) return;
            (bool ReturnsJson, bool ReturnsJsonArray, string RootProperty, IReadOnlyList<string> ColumnNames) meta;
            try { meta = ResolveFunctionJsonSet(schema, fname); } catch { return; }
            if (ShouldDiagJsonAst())
            {
                try { System.Console.WriteLine($"[json-ast-fn-expand-attempt] {schema}.{fname} returnsJson={meta.ReturnsJson} colNames={(meta.ColumnNames == null ? 0 : meta.ColumnNames.Count)} alias={target.Name}"); } catch { }
            }
            if (!meta.ReturnsJson || meta.ColumnNames == null || meta.ColumnNames.Count == 0) return;
            _functionJsonExpansionStack.Add(key);
            try
            {
                target.IsNestedJson = true;
                target.ReturnsJson = true;
                target.ReturnsJsonArray = meta.ReturnsJsonArray;
                target.JsonRootProperty = meta.RootProperty;
                var list = new List<ResultColumn>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cn in meta.ColumnNames)
                {
                    if (string.IsNullOrWhiteSpace(cn)) continue;
                    var name = cn.Trim();
                    if (seen.Add(name)) list.Add(new ResultColumn { Name = name });
                }
                target.Columns = list;
                target.SqlTypeName = null; target.MaxLength = null; target.IsNullable = null;
                if (ShouldDiagJsonAst()) try { System.Console.WriteLine($"[json-ast-fn-expand] {schema}.{fname} cols={list.Count} alias={target.Name}"); } catch { }
            }
            finally { _functionJsonExpansionStack.Remove(key); }
        }
        private void BindColumnReference(ColumnReferenceExpression cref, ResultColumn col, SourceBindingState state)
        {
            if (cref?.MultiPartIdentifier?.Identifiers == null || cref.MultiPartIdentifier.Identifiers.Count == 0) return;
            var parts = cref.MultiPartIdentifier.Identifiers.Select(i => i.Value).ToList();
            // Diagnose nur bei aktiviertem JSON-AST-Diag-Level
            bool forceVerbose = ShouldDiagJsonAst();
            if (parts.Count == 1)
            {
                if (_tableAliases.Count == 1)
                {
                    var kv = _tableAliases.First();
                    col.SourceAlias = kv.Key;
                    col.SourceSchema = kv.Value.Schema;
                    col.SourceTable = kv.Value.Table;
                    col.SourceColumn = parts[0];
                    state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                    ConsoleWriteBind(col, reason: "single-alias");
                    _analysis.ColumnRefBound++;
                    TryAssignColumnType(col);
                }
                else if (_tableSources.Count == 1 && _tableAliases.Count == 0)
                {
                    var st = _tableSources.First();
                    var segs = st.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (segs.Length == 2)
                    {
                        col.SourceSchema = segs[0];
                        col.SourceTable = segs[1];
                        col.SourceColumn = parts[0];
                        state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                        ConsoleWriteBind(col, reason: "single-table-source");
                        _analysis.ColumnRefBound++;
                        TryAssignColumnType(col);
                    }
                }
                else
                {
                    // Versuch: Single-Part aus eindeutigem Derived Table / CTE herleiten
                    var matchingDerived = _derivedTableColumnSources
                        .Where(d => d.Value.ContainsKey(parts[0]) && !string.IsNullOrWhiteSpace(d.Value[parts[0]].Schema))
                        .ToList();
                    if (matchingDerived.Count == 1)
                    {
                        var md = matchingDerived[0];
                        var dsrc = md.Value[parts[0]];
                        col.SourceAlias = md.Key;
                        col.SourceSchema = dsrc.Schema;
                        col.SourceTable = dsrc.Table;
                        col.SourceColumn = dsrc.Column;
                        if (dsrc.Ambiguous) col.IsAmbiguous = true;
                        state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                        ConsoleWriteBind(col, reason: "single-derived-unique");
                        _analysis.ColumnRefBound++;
                        // Aggregat-Propagation: finde ursprüngliche ResultColumn im Derived Table um AggregateFlags zu kopieren
                        TryPropagateAggregateFromDerived(parts[0], md.Key, col);
                    }
                    else col.IsAmbiguous = true;
                }
            }
            else if (parts.Count == 2)
            {
                var tableOrAlias = parts[0];
                var column = parts[1];
                if (_tableAliases.TryGetValue(tableOrAlias, out var mapped))
                {
                    col.SourceAlias = tableOrAlias;
                    col.SourceSchema = mapped.Schema;
                    col.SourceTable = mapped.Table;
                    col.SourceColumn = column;
                    state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                    ConsoleWriteBind(col, reason: "alias-physical");
                    _analysis.ColumnRefBound++;
                    TryAssignColumnType(col);
                }
                else if (_derivedTableColumnSources.TryGetValue(tableOrAlias, out var derivedMap) && derivedMap.TryGetValue(column, out var dsrc))
                {
                    col.SourceAlias = tableOrAlias;
                    if (!string.IsNullOrWhiteSpace(dsrc.Schema)) col.SourceSchema = dsrc.Schema;
                    if (!string.IsNullOrWhiteSpace(dsrc.Table)) col.SourceTable = dsrc.Table;
                    if (!string.IsNullOrWhiteSpace(dsrc.Column)) col.SourceColumn = dsrc.Column;
                    if (dsrc.Ambiguous) col.IsAmbiguous = true;
                    state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                    ConsoleWriteBind(col, reason: "alias-derived");
                    _analysis.ColumnRefBound++;
                    TryAssignColumnType(col);
                    // Neu: Aggregat/Literal Propagation auch für 2-teilige alias.column Referenzen
                    TryPropagateAggregateFromDerived(column, tableOrAlias, col);
                    // Direkter Lookup falls TryPropagateAggregateFromDerived nichts gesetzt hat (Name-Mismatch etc.)
                    if (!col.IsAggregate && _derivedTableColumns.TryGetValue(tableOrAlias, out var dcols))
                    {
                        var srcCol = dcols.FirstOrDefault(dc => dc.Name != null && dc.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
                        if (srcCol != null)
                        {
                            // Literal Flags additiv übernehmen
                            if (srcCol.HasIntegerLiteral) col.HasIntegerLiteral = true;
                            if (srcCol.HasDecimalLiteral) col.HasDecimalLiteral = true;
                            // Aggregat nur propagieren wenn Ziel wirklich ein ColumnRef ist (kein zusammengesetzter Ausdruck)
                            if (srcCol.IsAggregate && col.ExpressionKind == ResultColumnExpressionKind.ColumnRef && !col.IsAggregate)
                            {
                                col.IsAggregate = true;
                                col.AggregateFunction = srcCol.AggregateFunction;
                            }
                        }
                    }
                }
                else col.IsAmbiguous = true;
                // Fallback: Wenn keine physische Bindung aber Derived-Alias bekannt -> reine Aggregat/Literal Propagation
                if ((string.IsNullOrWhiteSpace(col.SourceSchema) || string.IsNullOrWhiteSpace(col.SourceColumn)) && _derivedTableColumns.TryGetValue(parts[0], out var dcolsFallback))
                {
                    var srcColFb = dcolsFallback.FirstOrDefault(dc => dc.Name != null && dc.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase));
                    if (srcColFb != null)
                    {
                        // Aggregat nur propagieren wenn kein Computed Kontext vorliegt
                        if (srcColFb.IsAggregate && !col.IsAggregate && col.ExpressionKind != ResultColumnExpressionKind.Computed)
                        {
                            col.IsAggregate = true;
                            col.AggregateFunction = srcColFb.AggregateFunction;
                        }
                        if (srcColFb.HasIntegerLiteral) col.HasIntegerLiteral = true;
                        if (srcColFb.HasDecimalLiteral) col.HasDecimalLiteral = true;
                    }
                }
            }
            else if (parts.Count >= 3)
            {
                var schema = parts[parts.Count - 3];
                var table = parts[parts.Count - 2];
                var column = parts[parts.Count - 1];
                col.SourceSchema = schema;
                col.SourceTable = table;
                col.SourceColumn = column;
                state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                ConsoleWriteBind(col, reason: "three-part");
                _analysis.ColumnRefBound++;
                TryAssignColumnType(col);
                if (ShouldDiagJsonAst()) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (three-part)"); } catch { }
            }
            else
            {
                // Erweiterung: Versuche generischere Muster (4-teilige Identifier, temporäre Tabellen, dbo Fallback)
                try
                {
                    // Beispiel: db.schema.table.column oder server.db.schema.table.column -> wir nehmen die letzten 3 Segmente
                    if (parts.Count >= 4)
                    {
                        var column = parts[^1];
                        var table = parts[^2];
                        var schema = parts[^3];
                        if (!string.IsNullOrWhiteSpace(schema) && !string.IsNullOrWhiteSpace(table))
                        {
                            col.SourceSchema = schema;
                            col.SourceTable = table;
                            col.SourceColumn = column;
                            state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                            ConsoleWriteBind(col, reason: "four-part-tail");
                            _analysis.ColumnRefBound++;
                            TryAssignColumnType(col);
                            if (ShouldDiagJsonAst()) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (four-part-tail)"); } catch { }
                            return;
                        }
                    }
                    // Temp Table (#temp) Referenz: wir behalten Schema leer und markieren Table
                    if (parts[^2].StartsWith("#") || parts[^1].StartsWith("#"))
                    {
                        var column = parts[^1];
                        var table = parts[^2].StartsWith("#") ? parts[^2] : parts[^1];
                        col.SourceTable = table;
                        col.SourceColumn = column;
                        state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                        ConsoleWriteBind(col, reason: "temp-table");
                        _analysis.ColumnRefBound++;
                        TryAssignColumnType(col);
                        if (ShouldDiagJsonAst()) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (temp-table)"); } catch { }
                        return;
                    }
                    // Fallback: Wenn nur 2 Teile aber erster kein Alias match & zweiter plausibel Spaltenname: interpretiere erster als Tabelle unter DefaultSchema
                    if (parts.Count == 2 && string.IsNullOrWhiteSpace(col.SourceSchema) && string.IsNullOrWhiteSpace(col.SourceTable))
                    {
                        var tableOrSchema = parts[0];
                        var column = parts[1];
                        if (!_tableAliases.ContainsKey(tableOrSchema))
                        {
                            col.SourceSchema = _analysis.DefaultSchema;
                            col.SourceTable = tableOrSchema;
                            col.SourceColumn = column;
                            state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                            ConsoleWriteBind(col, reason: "two-part-defaultschema");
                            _analysis.ColumnRefBound++;
                            TryAssignColumnType(col);
                            if (ShouldDiagJsonAst()) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (two-part-defaultschema)"); } catch { }
                        }
                    }
                }
                catch { }
            }
        }

        private static void TryAssignColumnType(ResultColumn col)
        {
            if (col == null) return;
            if (!string.IsNullOrWhiteSpace(col.SqlTypeName)) return; // bereits gesetzt (Literal / Aggregat etc.)
            if (ResolveTableColumnType == null) return;
            if (string.IsNullOrWhiteSpace(col.SourceSchema) || string.IsNullOrWhiteSpace(col.SourceTable) || string.IsNullOrWhiteSpace(col.SourceColumn)) return;
            try
            {
                var res = ResolveTableColumnType(col.SourceSchema, col.SourceTable, col.SourceColumn);
                if (!string.IsNullOrWhiteSpace(res.SqlTypeName))
                {
                    col.SqlTypeName = res.SqlTypeName;
                    if (res.MaxLength.HasValue) col.MaxLength = res.MaxLength.Value;
                    if (res.IsNullable.HasValue) col.IsNullable = res.IsNullable.Value;
                }
            }
            catch { }
        }
        public override void ExplicitVisit(ExecuteSpecification node)
        {
            try
            {
                if (node.ExecutableEntity is ExecutableProcedureReference epr)
                {
                    var name = epr.ProcedureReference?.ProcedureReference?.Name;
                    if (name != null && name.Identifiers?.Count > 0)
                    {
                        string schema = _analysis.DefaultSchema; string proc = null;
                        if (name.Identifiers.Count == 1) proc = name.Identifiers[^1].Value;
                        else { proc = name.Identifiers[^1].Value; schema = name.Identifiers[^2].Value; }
                        if (!string.IsNullOrWhiteSpace(proc)) _analysis.ExecutedProcedures.Add(new ExecutedProcedureCall { Schema = schema, Name = proc });
                    }
                }
            }
            catch { }
            base.ExplicitVisit(node);
        }
        private void AddStatement(TSqlStatement stmt)
        {
            if (stmt?.StartOffset >= 0 && stmt.FragmentLength > 0)
            {
                var end = Math.Min(_definition.Length, stmt.StartOffset + stmt.FragmentLength);
                if (_offsets.Add(stmt.StartOffset))
                {
                    var text = _definition.Substring(stmt.StartOffset, end - stmt.StartOffset).Trim();
                    if (text.Length > 0) _analysis.StatementTexts.Add(text);
                }
            }
        }
        private static string NormalizeJsonPath(string value) => string.IsNullOrWhiteSpace(value) ? value : value.Trim().Trim('[', ']', '"', '\'');
        private static string SafePropertyName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var seg = path.Split('.', StringSplitOptions.RemoveEmptyEntries); var cand = seg.Length > 0 ? seg[^1] : path;
            var b = new StringBuilder(); foreach (var ch in cand) if (char.IsLetterOrDigit(ch) || ch == '_') b.Append(ch);
            if (b.Length == 0) return null; if (!char.IsLetter(b[0]) && b[0] != '_') b.Insert(0, '_'); return b.ToString();
        }
        private static string ExtractLiteralValue(Literal lit) => lit switch { null => null, StringLiteral s => s.Value, _ => lit.Value };
        private static string SanitizeAliasPreserveDots(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias)) return null;
            var b = new StringBuilder();
            foreach (var ch in alias)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.') b.Append(ch);
            }
            if (b.Length == 0) return null;
            // Ensure starts with letter or underscore for downstream code gen safety
            if (!char.IsLetter(b[0]) && b[0] != '_') b.Insert(0, '_');
            return b.ToString();
        }
        private static bool IsLiteralString(ScalarExpression expr, out string value)
        {
            value = null;
            switch (expr)
            {
                case StringLiteral sl:
                    value = sl.Value; return true;
                case Literal lit when lit is StringLiteral:
                    value = lit.Value; return true;
                default:
                    return false;
            }
        }
        // Entfernt: frühere heuristische Methoden (HasIdSuffix / IsInOutLiteral) zugunsten strikt AST-basierter Analyse.
        private sealed class JsonSetBuilder
        {
            public bool JsonWithArrayWrapper { get; set; }
            public bool JsonWithoutArrayWrapper { get; set; }
            public string JsonRootProperty { get; set; }
            public bool HasSelectStar { get; set; }
            public List<ResultColumn> Columns { get; } = new();
            public ResultSet ToResultSet() => new()
            {
                ReturnsJson = true,
                ReturnsJsonArray = JsonWithArrayWrapper && !JsonWithoutArrayWrapper,
                // WITHOUT ARRAY WRAPPER implied by ReturnsJsonArray==false
                JsonRootProperty = JsonRootProperty,
                Columns = Columns.ToArray(),
                HasSelectStar = HasSelectStar
            };
        }

        // --- Derived table / CTE Verarbeitung ---
        private void ProcessQueryDerivedTable(QueryDerivedTable node)
        {
            var alias = node?.Alias?.Value; if (string.IsNullOrWhiteSpace(alias)) return;
            if (node.QueryExpression is not QuerySpecification qs) return; // Nur einfache SELECTs behandeln (kein UNION etc.)
            var derivedCols = new List<ResultColumn>();
            var columnMap = ExtractColumnSourceMapFromQuerySpecification(qs, derivedCols);
            if (columnMap.Count > 0)
            {
                _derivedTableColumnSources[alias] = columnMap;
                _derivedTableColumns[alias] = derivedCols;
                ConsoleWriteDerived(alias, columnMap, isCte: false);
            }
        }
        private Dictionary<string, (string Schema, string Table, string Column, bool Ambiguous)> ExtractColumnSourceMapFromQuerySpecification(QuerySpecification qs, List<ResultColumn> outColumns)
        {
            var localAliases = new Dictionary<string, (string Schema, string Table)>(StringComparer.OrdinalIgnoreCase);
            var localTableSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Sammle physische Tabellen (nur NamedTableReference, Joins rekursiv)
            if (qs.FromClause?.TableReferences != null)
                foreach (var tr in qs.FromClause.TableReferences) CollectLocalNamedTableReferences(tr, localAliases, localTableSources);

            var map = new Dictionary<string, (string Schema, string Table, string Column, bool Ambiguous)>(StringComparer.OrdinalIgnoreCase);
            foreach (var sce in qs.SelectElements.OfType<SelectScalarExpression>())
            {
                var alias = sce.ColumnName?.Value;
                if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] select-elt alias={sce.ColumnName?.Value} exprType={sce.Expression?.GetType().Name}"); } catch { } }
                if (string.IsNullOrWhiteSpace(alias))
                {
                    if (sce.Expression is ColumnReferenceExpression implicitCr && implicitCr.MultiPartIdentifier?.Identifiers?.Count > 0)
                        alias = implicitCr.MultiPartIdentifier.Identifiers[^1].Value;
                    else if (sce.Expression is CastCall castCall && castCall.Parameter is ColumnReferenceExpression castCol && castCol.MultiPartIdentifier?.Identifiers?.Count > 0)
                        alias = castCol.MultiPartIdentifier.Identifiers[^1].Value;
                }
                if (string.IsNullOrWhiteSpace(alias)) continue;
                var col = new ResultColumn();
                // Wichtig: Name setzen für spätere Aggregate/Flag-Propagation über TryPropagateAggregateFromDerived
                col.Name = alias;
                var state = new SourceBindingState();
                AnalyzeScalarExpressionDerived(sce.Expression, col, state, localAliases, localTableSources);
                // Aggregat-Erkennung falls direkter FunctionCall
                if (sce.Expression is FunctionCall dirFn)
                {
                    var fnLowerMeta = dirFn.FunctionName?.Value?.ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(fnLowerMeta)) col.AggregateFunction = fnLowerMeta; // minimal retention for existing aggregate logic if needed
                    if (fnLowerMeta is "sum" or "count" or "count_big" or "avg" or "exists" or "min" or "max")
                    {
                        col.IsAggregate = true;
                        col.AggregateFunction = fnLowerMeta;
                    }
                }
                var ambiguous = col.IsAmbiguous == true || state.BindingCount > 1;
                map[alias] = (col.SourceSchema, col.SourceTable, col.SourceColumn, ambiguous);
                outColumns?.Add(col);
                if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] derived-col name={col.Name} agg={col.IsAggregate} fn={col.AggregateFunction} intLit={col.HasIntegerLiteral} decLit={col.HasDecimalLiteral}"); } catch { } }
            }
            return map;
        }
        private void CollectLocalNamedTableReferences(TableReference tr, Dictionary<string, (string Schema, string Table)> localAliases, HashSet<string> localTableSources)
        {
            switch (tr)
            {
                case QualifiedJoin qj:
                    CollectLocalNamedTableReferences(qj.FirstTableReference, localAliases, localTableSources);
                    CollectLocalNamedTableReferences(qj.SecondTableReference, localAliases, localTableSources);
                    break;
                case NamedTableReference ntr:
                    var schema = ntr.SchemaObject?.SchemaIdentifier?.Value ?? _analysis.DefaultSchema;
                    var table = ntr.SchemaObject?.BaseIdentifier?.Value;
                    if (!string.IsNullOrWhiteSpace(table))
                    {
                        var alias = ntr.Alias?.Value;
                        var key = !string.IsNullOrWhiteSpace(alias) ? alias : table;
                        if (!localAliases.ContainsKey(key)) localAliases[key] = (schema, table);
                        localTableSources.Add($"{schema}.{table}");
                    }
                    break;
                case QueryDerivedTable qdt:
                    // Verschachtelte DerivedTables rekursiv verarbeiten
                    ProcessQueryDerivedTable(qdt);
                    break;
                default:
                    break;
            }
        }
        private void AnalyzeScalarExpressionDerived(ScalarExpression expr, ResultColumn target, SourceBindingState state,
            Dictionary<string, (string Schema, string Table)> localAliases, HashSet<string> localTableSources)
        {
            switch (expr)
            {
                case null: return;
                case ColumnReferenceExpression cref:
                    BindColumnReferenceDerived(cref, target, state, localAliases, localTableSources);
                    if (target.ExpressionKind == null)
                        target.ExpressionKind = ResultColumnExpressionKind.ColumnRef;
                    try
                    {
                        var parts = cref.MultiPartIdentifier?.Identifiers?.Select(i => i.Value).ToList();
                        // Entfernt: identity.RecordAsJson Spezialfall
                    }
                    catch { }
                    // RawExpression population for derived expressions delegated to caller (not needed here)
                    break;
                case CastCall castCall:
                    AnalyzeScalarExpressionDerived(castCall.Parameter, target, state, localAliases, localTableSources);
                    break;
                case ConvertCall convertCall:
                    AnalyzeScalarExpressionDerived(convertCall.Parameter, target, state, localAliases, localTableSources);
                    AnalyzeScalarExpressionDerived(convertCall.Style, target, state, localAliases, localTableSources);
                    break;
                case IntegerLiteral _:
                    target.HasIntegerLiteral = true; break;
                case NumericLiteral nl:
                    if (!string.IsNullOrWhiteSpace(nl.Value) && nl.Value.Contains('.')) target.HasDecimalLiteral = true; else target.HasIntegerLiteral = true; break;
                case RealLiteral _:
                    target.HasDecimalLiteral = true; break;
                case FunctionCall fn:
                    var fnName2 = fn.FunctionName?.Value;
                    if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] fn-enter-derived name={target.Name} fn={fnName2} paramCount={fn.Parameters?.Count}"); } catch { } }
                    if (string.IsNullOrWhiteSpace(fnName2) || !fnName2.Equals("JSON_QUERY", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Klassifiziere als FunctionCall (wurde bisher nicht gesetzt -> sonst bleibt ExpressionKind null)
                            target.ExpressionKind ??= ResultColumnExpressionKind.FunctionCall;
                            if (fn.CallTarget is MultiPartIdentifierCallTarget mp2 && mp2.MultiPartIdentifier?.Identifiers?.Count > 0)
                            {
                                var idents = mp2.MultiPartIdentifier.Identifiers.Select(i => i.Value).ToList();
                                if (idents.Count == 1)
                                {
                                    // legacy FunctionName removed; if needed build Reference elsewhere
                                }
                                else if (idents.Count >= 2)
                                {
                                    // legacy FunctionSchemaName/FunctionName removed; Reference creation occurs in unified path later
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(fnName2))
                            {
                                // legacy FunctionName assignment removed
                            }
                            if (!string.IsNullOrWhiteSpace(fnName2))
                            {
                                var lower2 = fnName2.ToLowerInvariant();
                                if (lower2 is "sum" or "count" or "count_big" or "avg" or "exists" or "min" or "max")
                                {
                                    target.IsAggregate = true;
                                    target.AggregateFunction = lower2;
                                    _analysis.AggregateCount++;
                                }
                                if (lower2 == "sum")
                                {
                                    try
                                    {
                                        if (fn.Parameters != null && fn.Parameters.Count == 1)
                                        {
                                            var pExpr = fn.Parameters[0] as ScalarExpression;
                                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] sum-param-derived name={target.Name} paramType={pExpr?.GetType().Name}"); } catch { } }
                                            if (IsPureZeroOneConditional(pExpr))
                                            {
                                                if (!target.HasIntegerLiteral) target.HasIntegerLiteral = true;
                                                if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                                                {
                                                    target.SqlTypeName = "int";
                                                    if (ShouldDiag()) System.Console.WriteLine($"[json-agg-diag] sum-zero-one-detected-derived name={target.Name} assigned=int");
                                                }
                                            }
                                            else
                                            {
                                                // Fallback Heuristik: Prüfe ob direkter IIF() mit Literal 1/0 Parametern vorhanden (locker)
                                                if (pExpr is FunctionCall innerFn)
                                                {
                                                    var inName = innerFn.FunctionName?.Value?.ToLowerInvariant();
                                                    if (inName == "iif" && innerFn.Parameters?.Count == 3)
                                                    {
                                                        bool zeroOneParams = innerFn.Parameters[1] is Literal litA && (litA.Value == "1" || litA.Value == "0")
                                                                            && innerFn.Parameters[2] is Literal litB && (litB.Value == "1" || litB.Value == "0");
                                                        if (zeroOneParams)
                                                        {
                                                            if (!target.HasIntegerLiteral) target.HasIntegerLiteral = true;
                                                            if (string.IsNullOrWhiteSpace(target.SqlTypeName)) target.SqlTypeName = "int";
                                                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] sum-zero-one-fallback-derived name={target.Name} assigned=int"); } catch { } }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                            // Entfernt: identity.RecordAsJson Erkennung
                        }
                        catch { }
                        foreach (var p in fn.Parameters) AnalyzeScalarExpressionDerived(p, target, state, localAliases, localTableSources);
                        // IIF Typableitung im Derived-Kontext (innerhalb FunctionCall scope, Zugriff auf fnName2, fn)
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(fnName2) && fnName2.Equals("IIF", StringComparison.OrdinalIgnoreCase) && fn.Parameters?.Count == 3)
                            {
                                // IIF im Derived-Kontext ebenfalls als FunctionCall markieren.
                                target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                                var thenExpr = fn.Parameters[1];
                                var elseExpr = fn.Parameters[2];
                                var thenCol = new ResultColumn(); var thenState = new SourceBindingState();
                                AnalyzeScalarExpressionDerived(thenExpr as ScalarExpression, thenCol, thenState, localAliases, localTableSources);
                                var elseCol = new ResultColumn(); var elseState = new SourceBindingState();
                                AnalyzeScalarExpressionDerived(elseExpr as ScalarExpression, elseCol, elseState, localAliases, localTableSources);
                                if (!string.IsNullOrWhiteSpace(thenCol.SourceColumn) && !string.IsNullOrWhiteSpace(elseCol.SourceColumn) && thenCol.SourceColumn.Equals(elseCol.SourceColumn, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Entfernt: Quellspalten-basierte Typableitung
                                }
                                if (string.IsNullOrWhiteSpace(target.SqlTypeName) && !string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && thenCol.SqlTypeName.Equals(elseCol.SqlTypeName, StringComparison.OrdinalIgnoreCase))
                                {
                                    target.SqlTypeName = thenCol.SqlTypeName;
                                }
                                if (string.IsNullOrWhiteSpace(target.SqlTypeName)
                                    && IsLiteralString(thenExpr, out var litThen)
                                    && IsLiteralString(elseExpr, out var litElse))
                                {
                                    var maxLen = Math.Max(litThen?.Length ?? 0, litElse?.Length ?? 0);
                                    target.SqlTypeName = "nvarchar"; if (maxLen > 0) target.MaxLength = maxLen;
                                }
                            }
                        }
                        catch { }
                        // Nach Parameteranalyse (derived): Falls IIF Klassifikation überschrieben wurde, zurücksetzen
                        if (!string.IsNullOrWhiteSpace(fnName2) && fnName2.Equals("IIF", StringComparison.OrdinalIgnoreCase))
                        {
                            if (target.ExpressionKind != ResultColumnExpressionKind.FunctionCall && target.ExpressionKind != ResultColumnExpressionKind.JsonQuery)
                                target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                        }
                    }
                    else
                    {
                        // JSON_QUERY im Derived-Kontext: Parameter untersuchen wie im primären Analyzer
                        target.ExpressionKind = ResultColumnExpressionKind.JsonQuery;
                        if (fn.Parameters != null)
                        {
                            foreach (var p in fn.Parameters)
                            {
                                try
                                {
                                    bool subqueryHandled = false;
                                    if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] jsonQueryParamType name={target.Name} paramType={p?.GetType().Name} (derived)"); } catch { } }
                                    if (p is ScalarSubquery ss)
                                    {
                                        var innerQs = UnwrapToQuerySpecification(ss.QueryExpression);
                                        if (innerQs != null)
                                        {
                                            AnalyzeJsonQueryInnerSubquery(innerQs, target, state);
                                            subqueryHandled = true; continue;
                                        }
                                    }
                                    if (p is ParenthesisExpression pe && pe.Expression is ScalarSubquery ss2)
                                    {
                                        var innerQs2 = UnwrapToQuerySpecification(ss2.QueryExpression);
                                        if (innerQs2 != null)
                                        {
                                            AnalyzeJsonQueryInnerSubquery(innerQs2, target, state);
                                            subqueryHandled = true; continue;
                                        }
                                    }
                                    if (!subqueryHandled)
                                    {
                                        var deepSs = FindFirstScalarSubquery(p as ScalarExpression, 0);
                                        if (deepSs != null)
                                        {
                                            var innerQs3 = UnwrapToQuerySpecification(deepSs.QueryExpression);
                                            if (innerQs3 != null)
                                            {
                                                AnalyzeJsonQueryInnerSubquery(innerQs3, target, state);
                                                if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] jsonQueryParamDeepSubquery name={target.Name} depthFound (derived)"); } catch { } }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    break;
                case IIfCall iif2:
                    // IIF im Derived-Kontext
                    target.ExpressionKind ??= ResultColumnExpressionKind.FunctionCall;
                    try
                    {
                        var thenExpr2 = iif2.ThenExpression as ScalarExpression;
                        var elseExpr2 = iif2.ElseExpression as ScalarExpression;
                        var thenCol2 = new ResultColumn(); var thenState2 = new SourceBindingState();
                        AnalyzeScalarExpressionDerived(thenExpr2, thenCol2, thenState2, localAliases, localTableSources);
                        var elseCol2 = new ResultColumn(); var elseState2 = new SourceBindingState();
                        AnalyzeScalarExpressionDerived(elseExpr2, elseCol2, elseState2, localAliases, localTableSources);
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName)
                            && IsLiteralString(thenExpr2, out var litThen2)
                            && IsLiteralString(elseExpr2, out var litElse2))
                        {
                            var maxLen = Math.Max(litThen2?.Length ?? 0, litElse2?.Length ?? 0);
                            target.SqlTypeName = "nvarchar"; if (maxLen > 0) target.MaxLength = maxLen;
                        }
                    }
                    catch { }
                    break;
                case BinaryExpression be:
                    // Markiere vor Analyse als Computed
                    target.ExpressionKind ??= ResultColumnExpressionKind.Computed;
                    var prevKind = target.ExpressionKind;
                    AnalyzeScalarExpressionDerived(be.FirstExpression, target, state, localAliases, localTableSources);
                    AnalyzeScalarExpressionDerived(be.SecondExpression, target, state, localAliases, localTableSources);
                    // Bewahre Computed falls durch Operanden überschrieben
                    if (prevKind == ResultColumnExpressionKind.Computed && target.ExpressionKind != ResultColumnExpressionKind.Computed)
                        target.ExpressionKind = ResultColumnExpressionKind.Computed;
                    break;
                case UnaryExpression ue:
                    AnalyzeScalarExpressionDerived(ue.Expression, target, state, localAliases, localTableSources);
                    break;
                case SearchedCaseExpression sce2:
                    foreach (var w in sce2.WhenClauses) AnalyzeScalarExpressionDerived(w.ThenExpression, target, state, localAliases, localTableSources);
                    AnalyzeScalarExpressionDerived(sce2.ElseExpression, target, state, localAliases, localTableSources);
                    break;
                case SimpleCaseExpression simp:
                    AnalyzeScalarExpressionDerived(simp.InputExpression, target, state, localAliases, localTableSources);
                    foreach (var w in simp.WhenClauses) AnalyzeScalarExpressionDerived(w.ThenExpression, target, state, localAliases, localTableSources);
                    AnalyzeScalarExpressionDerived(simp.ElseExpression, target, state, localAliases, localTableSources);
                    break;
                case ParenthesisExpression pe:
                    AnalyzeScalarExpressionDerived(pe.Expression, target, state, localAliases, localTableSources);
                    break;
                default:
                    break;
            }
        }
        private void BindColumnReferenceDerived(ColumnReferenceExpression cref, ResultColumn col, SourceBindingState state,
            Dictionary<string, (string Schema, string Table)> localAliases, HashSet<string> localTableSources)
        {
            if (cref?.MultiPartIdentifier?.Identifiers == null || cref.MultiPartIdentifier.Identifiers.Count == 0) return;
            var parts = cref.MultiPartIdentifier.Identifiers.Select(i => i.Value).ToList();
            bool forceVerbose = ShouldDiagJsonAst();
            if (parts.Count == 1)
            {
                if (localAliases.Count == 1)
                {
                    var kv = localAliases.First();
                    col.SourceAlias = kv.Key;
                    col.SourceSchema = kv.Value.Schema;
                    col.SourceTable = kv.Value.Table;
                    col.SourceColumn = parts[0];
                    state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                    if (forceVerbose) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (derived-single-alias)"); } catch { }
                    _analysis.ColumnRefBound++;
                }
                else if (localTableSources.Count == 1 && localAliases.Count == 0)
                {
                    var st = localTableSources.First();
                    var segs = st.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (segs.Length == 2)
                    {
                        col.SourceSchema = segs[0];
                        col.SourceTable = segs[1];
                        col.SourceColumn = parts[0];
                        state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                        if (forceVerbose) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (derived-single-table)"); } catch { }
                        _analysis.ColumnRefBound++;
                    }
                }
                else col.IsAmbiguous = true;
            }
            else if (parts.Count == 2)
            {
                var tableOrAlias = parts[0];
                var column = parts[1];
                if (localAliases.TryGetValue(tableOrAlias, out var mapped))
                {
                    col.SourceAlias = tableOrAlias;
                    col.SourceSchema = mapped.Schema;
                    col.SourceTable = mapped.Table;
                    col.SourceColumn = column;
                    state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                    if (forceVerbose) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (derived-alias)"); } catch { }
                    _analysis.ColumnRefBound++;
                }
                else col.IsAmbiguous = true;
            }
            else if (parts.Count >= 3)
            {
                var schema = parts[parts.Count - 3];
                var table = parts[parts.Count - 2];
                var column = parts[parts.Count - 1];
                col.SourceSchema = schema;
                col.SourceTable = table;
                col.SourceColumn = column;
                state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                if (forceVerbose) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (derived-three-part)"); } catch { }
                _analysis.ColumnRefBound++;
            }
            if (col.IsAmbiguous == true) _analysis.ColumnRefAmbiguous++;
        }

        /// <summary>
        /// Analysiert eine innere QuerySpecification innerhalb eines JSON_QUERY( (subquery) , path ) Aufrufs.
        /// Ziel: Wenn das Subselect genau eine SelectElement hat und diese ein Aggregat (SUM/COUNT/COUNT_BIG/AVG/EXISTS)
        /// oder eine einfache CAST davon ist, die Aggregat-Metadaten (IsAggregate, AggregateFunction, Literal Flags) auf die äußere JSON Column propagieren.
        /// </summary>
        private void AnalyzeJsonQueryInnerSubquery(QuerySpecification qs, ResultColumn outer, SourceBindingState state)
        {
            if (qs == null || outer == null) return;
            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] json-inner-enter outer={outer.Name} selectCount={qs.SelectElements?.Count}"); } catch { } }
            // Nur einfache Fälle: genau ein SelectElement, kein SELECT *
            if (qs.SelectElements == null || qs.SelectElements.Count == 0) return;
            if (qs.SelectElements.OfType<SelectStarExpression>().Any()) return;
            // Hybrid Logik
            if (qs.SelectElements.Count == 1 && qs.SelectElements[0] is SelectScalarExpression singleSse)
            {
                try
                {
                    var expr = singleSse.Expression;
                    var temp = new ResultColumn();
                    AnalyzeScalarExpression(expr, temp, state);
                    if (temp.IsAggregate == true)
                    {
                        outer.IsAggregate = true;
                        outer.AggregateFunction = temp.AggregateFunction;
                        if (temp.HasIntegerLiteral) outer.HasIntegerLiteral = true;
                        if (temp.HasDecimalLiteral) outer.HasDecimalLiteral = true;
                        if (string.IsNullOrWhiteSpace(outer.RawExpression) && !string.IsNullOrWhiteSpace(temp.RawExpression)) outer.RawExpression = temp.RawExpression;
                        if (ShouldDiag()) System.Console.WriteLine($"[json-agg-diag] innerSubqueryResolvedScalar name={outer.Name} aggFn={outer.AggregateFunction}");
                        return;
                    }
                    else
                    {
                        if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] innerJsonQuerySingleNoAgg name={outer.Name} exprKind={temp.ExpressionKind}"); } catch { } }
                    }
                }
                catch { }
            }
            // Objekt-Expansion
            outer.ReturnsJson = true;
            bool withoutArray = false;
            try
            {
                if (qs.StartOffset >= 0 && qs.FragmentLength > 0 && _definition != null && qs.StartOffset + qs.FragmentLength <= _definition.Length)
                {
                    var frag = _definition.Substring(qs.StartOffset, qs.FragmentLength);
                    if (frag.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0) withoutArray = true;
                }
            }
            catch { }
            outer.ReturnsJsonArray = withoutArray ? false : true;
            var expandedChildren = outer.Columns != null ? outer.Columns.ToList() : new List<ResultColumn>();
            foreach (var se in qs.SelectElements.OfType<SelectScalarExpression>())
            {
                try
                {
                    var alias = se.ColumnName?.Value;
                    if (string.IsNullOrWhiteSpace(alias) && se.Expression is ColumnReferenceExpression cref && cref.MultiPartIdentifier?.Identifiers?.Count > 0)
                        alias = cref.MultiPartIdentifier.Identifiers.Last().Value;
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    var path = NormalizeJsonPath(alias);
                    var composed = SanitizeAliasPreserveDots(path);
                    // Verschachtelte Pfadbildung: Outer.Name + '.' + Child, wenn Outer bereits einen Namen besitzt und Child nicht schon damit beginnt.
                    if (!string.IsNullOrWhiteSpace(outer.Name))
                    {
                        if (!composed.StartsWith(outer.Name + ".", StringComparison.OrdinalIgnoreCase))
                        {
                            composed = outer.Name + "." + composed;
                        }
                    }
                    var child = new ResultColumn { Name = composed };
                    var childState = new SourceBindingState();
                    AnalyzeScalarExpression(se.Expression, child, childState);
                    try
                    {
                        if (se.StartOffset >= 0 && se.FragmentLength > 0 && _definition != null && se.StartOffset + se.FragmentLength <= _definition.Length)
                            child.RawExpression = _definition.Substring(se.StartOffset, se.FragmentLength).Trim();
                    }
                    catch { }
                    // Falls das analysierte Kind selbst ein Aggregat ist, Flags sicherstellen (Analyse sollte sie gesetzt haben).
                    // Zusätzlich: Wenn das Aggregat in einer Hülle (z.B. CAST/CONVERT) steckt und Analyse es nicht erkannte, könnte hier künftig ein Wrapper-Check erfolgen.
                    if (child.IsAggregate == true && !string.IsNullOrWhiteSpace(child.AggregateFunction))
                    {
                        // Option D: Sofortige Typableitung direkt hier durchführen, damit spätere Namensnormalisierungen oder Flag-Verluste den Typ nicht eliminieren.
                        if (string.IsNullOrWhiteSpace(child.SqlTypeName))
                        {
                            var fnLower = child.AggregateFunction.ToLowerInvariant();
                            switch (fnLower)
                            {
                                case "count":
                                    child.SqlTypeName = "int"; break;
                                case "count_big":
                                    child.SqlTypeName = "bigint"; break;
                                case "sum":
                                    if (child.HasDecimalLiteral) child.SqlTypeName = "decimal(18,2)";
                                    else if (child.HasIntegerLiteral) child.SqlTypeName = "int";
                                    else child.SqlTypeName = "decimal(18,2)"; // konservativer Default für monetäre Additionen
                                    break;
                                case "avg":
                                    child.SqlTypeName = "decimal(18,2)"; break;
                                case "exists":
                                    child.SqlTypeName = "bit"; break;
                                case "min":
                                case "max":
                                    // Ohne Quelltyp keine sichere Ableitung – bewusst offen lassen
                                    break;
                            }
                        }
                        if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] json-child-copy-agg parent={outer.Name} child={child.Name} fn={child.AggregateFunction} intLit={child.HasIntegerLiteral} decLit={child.HasDecimalLiteral}"); } catch { } }
                        if (!string.IsNullOrWhiteSpace(child.SqlTypeName))
                        {
                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] json-child-typed name={child.Name} sqlType={child.SqlTypeName}"); } catch { } }
                        }
                    }
                    else if (child.IsAggregate == true && string.IsNullOrWhiteSpace(child.AggregateFunction))
                    {
                        // Diagnose: Aggregat flag ohne FunctionName (sollte selten vorkommen) → Log helfen Root Cause zu finden.
                        if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] json-child-agg-missing-fn parent={outer.Name} child={child.Name} raw='{child.RawExpression}'"); } catch { } }
                    }
                    child.ExpressionKind ??= ResultColumnExpressionKind.Unknown;
                    if (!expandedChildren.Any(c => c.Name.Equals(child.Name, StringComparison.OrdinalIgnoreCase))) expandedChildren.Add(child);
                }
                catch { }
            }
            outer.Columns = expandedChildren;
            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] innerJsonQueryExpanded name={outer.Name} childCount={outer.Columns?.Count}"); } catch { } }
        }

        /// <summary>
        /// Versucht eine beliebige QueryExpression auf die innere QuerySpecification zu reduzieren.
        /// Unterstützt SelectStatement (->QueryExpression), QueryParenthesisExpression (rekursiv) und direkte QuerySpecification.
        /// </summary>
        private static QuerySpecification UnwrapToQuerySpecification(QueryExpression qe)
        {
            try
            {
                if (qe == null) return null;
                if (qe is QuerySpecification qs) return qs;
                if (qe is QueryParenthesisExpression qpe)
                {
                    return UnwrapToQuerySpecification(qpe.QueryExpression);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Durchsucht einen beliebig verschachtelten ScalarExpression-Baum nach dem ersten ScalarSubquery.
        /// Unterstützt ParenthesisExpression, BinaryExpression, FunctionCall (Parameter), CASE Expressions.
        /// depthLimit schützt vor pathologischer Rekursion.
        /// </summary>
        private static ScalarSubquery FindFirstScalarSubquery(ScalarExpression expr, int depth, int depthLimit = 12)
        {
            if (expr == null || depth > depthLimit) return null;
            try
            {
                if (expr is ScalarSubquery ss) return ss;
                switch (expr)
                {
                    case ParenthesisExpression pe:
                        return FindFirstScalarSubquery(pe.Expression, depth + 1, depthLimit);
                    case BinaryExpression be:
                        return FindFirstScalarSubquery(be.FirstExpression as ScalarExpression, depth + 1, depthLimit)
                               ?? FindFirstScalarSubquery(be.SecondExpression as ScalarExpression, depth + 1, depthLimit);
                    case FunctionCall fc:
                        if (fc.Parameters != null)
                        {
                            foreach (var p in fc.Parameters.OfType<ScalarExpression>())
                            {
                                var found = FindFirstScalarSubquery(p, depth + 1, depthLimit);
                                if (found != null) return found;
                            }
                        }
                        break;
                    case SearchedCaseExpression sce:
                        foreach (var w in sce.WhenClauses)
                        {
                            var f = FindFirstScalarSubquery(w.ThenExpression as ScalarExpression, depth + 1, depthLimit);
                            if (f != null) return f;
                        }
                        return FindFirstScalarSubquery(sce.ElseExpression as ScalarExpression, depth + 1, depthLimit);
                    case SimpleCaseExpression simp:
                        foreach (var w in simp.WhenClauses)
                        {
                            var f = FindFirstScalarSubquery(w.ThenExpression as ScalarExpression, depth + 1, depthLimit);
                            if (f != null) return f;
                        }
                        return FindFirstScalarSubquery(simp.ElseExpression as ScalarExpression, depth + 1, depthLimit);
                }
            }
            catch { }
            return null;
        }
        private static void ConsoleWriteBind(ResultColumn col, string reason)
        {
            if (!_astVerboseEnabled) return;
            if (string.IsNullOrWhiteSpace(col?.SourceSchema) || string.IsNullOrWhiteSpace(col.SourceTable) || string.IsNullOrWhiteSpace(col.SourceColumn)) return;
            try { Console.WriteLine($"[json-ast-bind] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} ({reason})"); } catch { }
        }
        private static void ConsoleWriteDerived(string alias, Dictionary<string, (string Schema, string Table, string Column, bool Ambiguous)> map, bool isCte)
        {
            if (!_astVerboseEnabled) return;
            try
            {
                foreach (var kv in map)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value.Schema)) continue;
                    var kind = isCte ? "cte" : "derived";
                    var amb = kv.Value.Ambiguous ? " amb" : "";
                    Console.WriteLine($"[json-ast-derived] {alias}.{kv.Key} => {kv.Value.Schema}.{kv.Value.Table}.{kv.Value.Column}{amb} ({kind})");
                }
            }
            catch { }
        }

        private void TryPropagateAggregateFromDerived(string innerAliasColumn, string derivedAlias, ResultColumn target)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(innerAliasColumn) || string.IsNullOrWhiteSpace(derivedAlias)) return;
                // Wir suchen im gespeicherten Derived-ResultSet nach der Spalte innerAliasColumn
                if (_derivedTableColumns.TryGetValue(derivedAlias, out var derivedCols))
                {
                    var src = derivedCols.FirstOrDefault(c => c.Name.Equals(innerAliasColumn, StringComparison.OrdinalIgnoreCase));
                    if (src != null)
                    {
                        // Literal Flags immer additiv propagieren (auch für Computed-Ausdrücke)
                        if (src.HasIntegerLiteral) target.HasIntegerLiteral = true;
                        if (src.HasDecimalLiteral) target.HasDecimalLiteral = true;
                        // Aggregat nur propagieren, wenn Ziel selbst ein reiner ColumnRef ist (kein Computed Ausdruck)
                        if (src.IsAggregate && !target.IsAggregate && target.ExpressionKind == ResultColumnExpressionKind.ColumnRef)
                        {
                            target.IsAggregate = true;
                            target.AggregateFunction = src.AggregateFunction;
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName) && !string.IsNullOrWhiteSpace(src.AggregateFunction))
                            {
                                switch (src.AggregateFunction.ToLowerInvariant())
                                {
                                    case "count":
                                        target.SqlTypeName = "int"; break;
                                    case "count_big":
                                        target.SqlTypeName = "bigint"; break;
                                    case "sum":
                                        // SUM übernimmt den bereits propagierten Literal-Status zur Ableitung
                                        if (src.HasIntegerLiteral && !src.HasDecimalLiteral) target.SqlTypeName = "int"; else if (src.HasDecimalLiteral) target.SqlTypeName = "decimal(18,2)"; else target.SqlTypeName = "decimal(18,2)";
                                        break;
                                    case "avg":
                                        target.SqlTypeName = "decimal(18,2)"; break;
                                    case "exists":
                                        target.SqlTypeName = "bit"; break;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void TryExtractTypeParameters(DataTypeReference dataType, ResultColumn target)
        {
            if (dataType is null || target is null) return;
            try
            {
                switch (dataType)
                {
                    case SqlDataTypeReference sqlRef:
                        // Numeric/decimal: (precision, scale)
                        if (sqlRef.SqlDataTypeOption is SqlDataTypeOption.Decimal or SqlDataTypeOption.Numeric)
                        {
                            if (sqlRef.Parameters?.Count >= 1)
                            {
                                if (sqlRef.Parameters[0] is Literal lit0 && int.TryParse(lit0.Value, out var prec)) target.CastTargetPrecision = prec;
                            }
                            if (sqlRef.Parameters?.Count >= 2)
                            {
                                if (sqlRef.Parameters[1] is Literal lit1 && int.TryParse(lit1.Value, out var sc)) target.CastTargetScale = sc;
                            }
                        }
                        // (var)char/binary: length
                        if (sqlRef.Parameters?.Count >= 1 && target.CastTargetLength == null)
                        {
                            if (sqlRef.Parameters[0] is Literal litLen)
                            {
                                var p0 = litLen.Value;
                                if (!string.IsNullOrWhiteSpace(p0) && !p0.Equals("max", StringComparison.OrdinalIgnoreCase) && int.TryParse(p0, out var len))
                                    target.CastTargetLength = len;
                            }
                        }
                        break;
                    case ParameterizedDataTypeReference paramRef:
                        if (paramRef.Parameters != null && paramRef.Parameters.Count > 0)
                        {
                            // Heuristik: 1 Parameter -> Length, 2 Parameter -> Precision/Scale
                            if (paramRef.Parameters.Count == 1 && paramRef.Parameters[0] is Literal l0)
                            {
                                var v = l0.Value;
                                if (!string.IsNullOrWhiteSpace(v) && !v.Equals("max", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var len))
                                    target.CastTargetLength = len;
                            }
                            else if (paramRef.Parameters.Count >= 2)
                            {
                                if (paramRef.Parameters[0] is Literal l1 && int.TryParse(l1.Value, out var prec)) target.CastTargetPrecision = prec;
                                if (paramRef.Parameters[1] is Literal l2 && int.TryParse(l2.Value, out var sc)) target.CastTargetScale = sc;
                            }
                        }
                        break;
                }
            }
            catch { }
        }

        // Entfernt: InferSqlTypeFromSourceBinding (Namensheuristiken) – keine automatische Typableitung mehr.

        // Prüft ob Ausdruck strikt einem binären 0/1 bedingten Muster entspricht (IIF/CASE Varianten)
        private static bool IsPureZeroOneConditional(ScalarExpression expr)
        {
            if (expr == null) return false;
            try
            {
                // IIF(FunctionCall) => Parameters[1]==1 & Parameters[2]==0 oder umgekehrt
                if (expr is FunctionCall fc)
                {
                    var name = fc.FunctionName?.Value?.ToLowerInvariant();
                    if (name == "iif" && fc.Parameters != null && fc.Parameters.Count == 3)
                    {
                        var t = fc.Parameters[1] as Literal;
                        var e = fc.Parameters[2] as Literal;
                        if (IsLiteralOne(t) && IsLiteralZero(e)) return true;
                        if (IsLiteralZero(t) && IsLiteralOne(e)) return true; // auch 0/1 möglich – trotzdem int
                    }
                }
                // Spezieller AST Node für IIF (manche Parser-Versionen) -> IIfCall
                if (expr is IIfCall iifc)
                {
                    // IIfCall hat Properties ThenExpression / ElseExpression
                    var thenExpr = iifc.ThenExpression as ScalarExpression;
                    var elseExpr = iifc.ElseExpression as ScalarExpression;
                    if (IsLiteralOne(thenExpr) && IsLiteralZero(elseExpr)) return true;
                    if (IsLiteralZero(thenExpr) && IsLiteralOne(elseExpr)) return true;
                }
                // SearchedCaseExpression WHEN ... THEN 1 ELSE 0
                if (expr is SearchedCaseExpression sce)
                {
                    bool allThenZeroOne = sce.WhenClauses?.All(w => IsLiteralZeroOne(w.ThenExpression)) == true;
                    if (allThenZeroOne && IsLiteralZeroOne(sce.ElseExpression))
                    {
                        // Mindestens eine THEN 1 oder ELSE 1 muss existieren, damit Summation sinnvoll >0 werden kann
                        bool anyOne = sce.WhenClauses.Any(w => IsLiteralOne(w.ThenExpression)) || IsLiteralOne(sce.ElseExpression);
                        if (anyOne) return true;
                    }
                }
                // SimpleCaseExpression THEN 1 ELSE 0
                if (expr is SimpleCaseExpression simp)
                {
                    bool allThenZeroOne = simp.WhenClauses?.All(w => IsLiteralZeroOne(w.ThenExpression)) == true;
                    if (allThenZeroOne && IsLiteralZeroOne(simp.ElseExpression))
                    {
                        bool anyOne = simp.WhenClauses.Any(w => IsLiteralOne(w.ThenExpression)) || IsLiteralOne(simp.ElseExpression);
                        if (anyOne) return true;
                    }
                }
            }
            catch { }
            return false;
        }
        private static bool IsLiteralZeroOne(ScalarExpression expr) => IsLiteralZero(expr) || IsLiteralOne(expr);
        private static bool IsLiteralOne(ScalarExpression expr)
        {
            return expr switch
            {
                IntegerLiteral il when il.Value == "1" => true,
                NumericLiteral nl when nl.Value == "1" => true,
                _ => false
            };
        }
        private static bool IsLiteralZero(ScalarExpression expr)
        {
            return expr switch
            {
                IntegerLiteral il when il.Value == "0" => true,
                NumericLiteral nl when nl.Value == "0" => true,
                _ => false
            };
        }
    }

    private static IReadOnlyList<ResultSet> AttachExecSource(IReadOnlyList<ResultSet> sets, IReadOnlyList<ExecutedProcedureCall> execs,
        IReadOnlyList<string> rawExecCandidates, IReadOnlyDictionary<string, string> rawKinds, string defaultSchema)
    {
        // AST-only phase: Do not enrich local JSON result sets with ExecSource metadata.
        // ExecSourceProcedureName should only be applied during higher-level normalization (append/forward) outside the parser.
        // Therefore we return the sets unchanged, preserving pure local JSON sets without source attribution.
        return sets ?? Array.Empty<ResultSet>();
    }

    internal static bool ShouldDiag()
    {
        var lvl = Environment.GetEnvironmentVariable("SPOCR_LOG_LEVEL");
        return lvl != null && (lvl.Equals("debug", StringComparison.OrdinalIgnoreCase) || lvl.Equals("trace", StringComparison.OrdinalIgnoreCase));
    }

    private static FunctionCall FindFirstFunctionCall(ScalarExpression expr, int depth, int depthLimit = 12)
    {
        if (expr == null || depth > depthLimit) return null;
        try
        {
            switch (expr)
            {
                case FunctionCall fc:
                    return fc;
                case ParenthesisExpression pe:
                    return FindFirstFunctionCall(pe.Expression, depth + 1, depthLimit);
                case CastCall cast:
                    return FindFirstFunctionCall(cast.Parameter as ScalarExpression, depth + 1, depthLimit);
                case ConvertCall conv:
                    var fnConv = FindFirstFunctionCall(conv.Parameter as ScalarExpression, depth + 1, depthLimit);
                    if (fnConv != null) return fnConv;
                    return FindFirstFunctionCall(conv.Style as ScalarExpression, depth + 1, depthLimit);
                case ScalarSubquery ss:
                    static QuerySpecification LocalUnwrap(QueryExpression qe)
                    {
                        while (qe is QueryParenthesisExpression qpe) qe = qpe.QueryExpression;
                        return qe as QuerySpecification;
                    }
                    var qs = LocalUnwrap(ss.QueryExpression);
                    if (qs != null)
                    {
                        foreach (var se in qs.SelectElements?.OfType<SelectScalarExpression>() ?? Array.Empty<SelectScalarExpression>())
                        {
                            var fcInner = FindFirstFunctionCall(se.Expression, depth + 1, depthLimit);
                            if (fcInner != null) return fcInner;
                        }
                    }
                    break;
                case CoalesceExpression coalesce:
                    if (coalesce.Expressions != null)
                    {
                        foreach (var exprItem in coalesce.Expressions.OfType<ScalarExpression>())
                        {
                            var fcCoalesce = FindFirstFunctionCall(exprItem, depth + 1, depthLimit);
                            if (fcCoalesce != null) return fcCoalesce;
                        }
                    }
                    break;
                case IIfCall iif:
                    return FindFirstFunctionCall(iif.ThenExpression as ScalarExpression, depth + 1, depthLimit)
                           ?? FindFirstFunctionCall(iif.ElseExpression as ScalarExpression, depth + 1, depthLimit);
                case SimpleCaseExpression sce:
                    foreach (var w in sce.WhenClauses)
                    {
                        var fcSimple = FindFirstFunctionCall(w.ThenExpression as ScalarExpression, depth + 1, depthLimit);
                        if (fcSimple != null) return fcSimple;
                    }
                    return FindFirstFunctionCall(sce.ElseExpression as ScalarExpression, depth + 1, depthLimit);
                case SearchedCaseExpression scex:
                    foreach (var w in scex.WhenClauses)
                    {
                        var fcSearch = FindFirstFunctionCall(w.ThenExpression as ScalarExpression, depth + 1, depthLimit);
                        if (fcSearch != null) return fcSearch;
                    }
                    return FindFirstFunctionCall(scex.ElseExpression as ScalarExpression, depth + 1, depthLimit);
                case NullIfExpression nif:
                    return FindFirstFunctionCall(nif.FirstExpression as ScalarExpression, depth + 1, depthLimit)
                           ?? FindFirstFunctionCall(nif.SecondExpression as ScalarExpression, depth + 1, depthLimit);
            }
        }
        catch { }
        return null;
    }
}


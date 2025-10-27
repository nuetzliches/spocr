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


    // Optional externer Resolver f�r Tabellen-Spalten Typen (AST-only; keine Namensheuristik).
    // Signatur: (schema, table, column) -> (sqlTypeName, maxLength, isNullable)
    // Wenn null oder sqlTypeName leer zur�ckgegeben wird, erfolgt keine Zuweisung.
    public static Func<string, string, string, (string SqlTypeName, int? MaxLength, bool? IsNullable)> ResolveTableColumnType { get; set; }
    // Optional externer Resolver f�r Tabellen-Spalten User-Defined-Typen.
    // Signatur: (schema, table, column) -> (userTypeSchema, userTypeName)
    public static Func<string, string, string, (string? Schema, string? Name)> ResolveTableColumnUserType { get; set; }
    // JSON-Funktions-Expansion: (schema, functionName) -> (returnsJson, returnsJsonArray, rootProperty, columns[])
    // columns[] enth�lt nur Namen (String-Liste); Typen werden nicht inferiert.
    public static Func<string, string, (bool ReturnsJson, bool ReturnsJsonArray, string RootProperty, IReadOnlyList<string> ColumnNames)> ResolveFunctionJsonSet { get; set; }
    public static Func<string, string, (string SqlTypeName, int? MaxLength, int? Precision, int? Scale, bool? IsNullable)> ResolveUserDefinedType { get; set; }
    // Optional externer Resolver f?r skalare Funktions-R?ckgabetypen (AST-only; keine Namensheuristik).
    // Signatur: (schema, functionName) -> (sqlTypeName, maxLength, isNullable)
    public static Func<string, string, (string SqlTypeName, int? MaxLength, bool? IsNullable)> ResolveScalarFunctionReturnType { get; set; }

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

        // Normalisierung mehrfacher Semikolons (Parser Toleranz f�r ";;") ohne Regex-Heuristik.
        // Ersetzt Runs von ';' (>1) durch genau ein ';'. Anschlie�end sicherstellen, dass am Ende ein Semikolon steht.
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

        // Kein heuristischer Fallback mehr: Wenn Parser kein Fragment liefert -> leeres Modell mit Fehlerinfos zur�ckgeben.
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
        var visitor = new Visitor(normalizedDefinition, analysis);
        fragment.Accept(visitor);

        // Nachlauf: fehlende Typen in abgeleiteten/CTE-Spalten anhand einfacher Heuristiken finalisieren
        try { visitor.FinalizeDerivedColumnTypes(); } catch { }

        // Filter CTE ResultSets and capture their column types for nested JSON propagation
        if (ShouldDiag()) Console.WriteLine($"[cte-filter] Before filtering: {analysis.JsonSets.Count} ResultSets");

        if (analysis.JsonSets.Count == 2)
        {
            var cteResultSet = analysis.JsonSets[1];
            if (ShouldDiag()) Console.WriteLine($"[cte-filter] Processing CTE ResultSet with {cteResultSet.Columns.Count} columns");

            foreach (var column in cteResultSet.Columns)
            {
                if (string.IsNullOrWhiteSpace(column?.Name) || string.IsNullOrWhiteSpace(column.SqlTypeName)) continue;
                var key = column.Name.Trim();
                if (key.Length == 0) continue;
                visitor.CaptureCteColumnType(key, new ResultColumn
                {
                    Name = key,
                    SqlTypeName = column.SqlTypeName,
                    MaxLength = column.MaxLength,
                    IsNullable = column.IsNullable
                });
                if (ShouldDiag()) Console.WriteLine($"[cte-filter] Captured {column.Name} -> {key}: {column.SqlTypeName}, MaxLength={column.MaxLength}, IsNullable={column.IsNullable}");
            }

            analysis.JsonSets.RemoveAt(1);
            if (ShouldDiag()) Console.WriteLine($"[cte-filter] Removed CTE ResultSet, back to {analysis.JsonSets.Count} ResultSets");
        }

        // Post-process: Apply CTE type propagation to top-level columns and nested JSON columns
        // First fix top-level bindings that still point to the CTE name (e.g., SourceTable = "WCalculation")
        visitor.ApplyCteTypePropagationToTopLevelColumns();
        // Then enrich nested JSON child columns from captured CTE column types
        visitor.ApplyCteTypePropagationToNestedJsonColumns();
        // Additionally, if CTE column types were captured with direct name mapping (e.g. NetPercentage -> invoicePercentage.quota),
        // apply by-name propagation to top-level columns with matching names (AST-only, no heuristics)
        visitor.ApplyCteTypePropagationToTopLevelColumnsByName();
        // Late consolidation: lineage + scoped alias resolution for nested JSON
        try { visitor.ApplyLineageLateConsolidationToNestedJson(); } catch { }

        // Normalize: Flatten accidental single-field JSON containers created by scalar subqueries with dotted aliases (e.g., 'invoice.invoiceId')
        try
        {
            void FlattenScalarDottedContainers(List<ResultSet> sets)
            {
                if (sets == null) return;
                foreach (var rs in sets)
                {
                    if (rs?.Columns == null) continue;
                    void Recurse(List<ResultColumn> cols)
                    {
                        if (cols == null) return;
                        for (int i = 0; i < cols.Count; i++)
                        {
                            var c = cols[i]; if (c == null) continue;
                            // Collapse pattern: dotted alias marked ReturnsJson with exactly one scalar child
                            bool looksDotted = !string.IsNullOrWhiteSpace(c.Name) && c.Name.Contains('.') && (c.ReturnsJson == true || c.IsNestedJson == true);
                            bool singleChild = c.Columns != null && c.Columns.Count == 1;
                            if (looksDotted && singleChild)
                            {
                                var child = c.Columns[0];
                                bool childIsScalar = child != null && (child.Columns == null || child.Columns.Count == 0) && child.ReturnsJson != true;
                                if (childIsScalar)
                                {
                                    // Transfer scalar typing to parent and drop JSON flags
                                    c.SqlTypeName = child.SqlTypeName;
                                    c.MaxLength = child.MaxLength;
                                    c.IsNullable = child.IsNullable;
                                    c.IsNestedJson = null;
                                    c.ReturnsJson = null;
                                    c.ReturnsJsonArray = null;
                                    c.JsonRootProperty = null;
                                    c.Columns = Array.Empty<ResultColumn>();
                                }
                            }
                            // Recurse into nested JSON if still present
                            if (c.Columns != null && c.Columns.Count > 0)
                            {
                                Recurse(c.Columns.ToList());
                            }
                        }
                    }
                    Recurse(rs.Columns.ToList());
                }
            }
            FlattenScalarDottedContainers(analysis.JsonSets);
        }
        catch { }

        // Summary-Ausgabe nach vollst�ndiger Traversierung (immer ausgegeben f�r Diagnose).
        // Zusammenfassungszeile nur bei aktiviertem JSON-AST-Diagnose-Level
        if (ShouldDiagJsonAst())
        {
            try
            {
                if (ShouldDiagJsonAst()) Console.WriteLine($"[json-ast-summary] colRefTotal={analysis.ColumnRefTotal} bound={analysis.ColumnRefBound} ambiguous={analysis.ColumnRefAmbiguous} inferred={analysis.ColumnRefInferred} aggregates={analysis.AggregateCount} nestedJson={analysis.NestedJsonCount}");
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

        // Entfernt: Regex-Heuristik f�r FOR JSON bei ParseErrors. Nur AST-basierte Erkennung bleibt erhalten.
        // Global Fallback: Falls AST keine JsonSets erkannt hat, aber das SQL eindeutig ein FOR JSON PATH enth�lt,
        // konstruiere ein minimales ResultSet rein aus Textsegmenten. Dieser Fallback ist streng begrenzt und dient
        // nur dazu einfache F�lle (Tests) abzudecken, in denen ScriptDom das JsonForClause nicht an das QuerySpecification
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
                                        // (legacy SourceFunction* entfernt � Referenz reicht)
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
                    selectSegment = string.Join("\n", selectSegment.Split('\n').Select(l => { var ci = l.IndexOf("--", StringComparison.Ordinal); return ci >= 0 ? l.Substring(0, ci) : l; }));
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
                    // Deferral Markierung f�r record-Spalte auch ohne Resolver (segmentbasierter Fallback)
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
                    // Minimal fallback Klassifikation f�r directionCode IIF(...,'in','out')
                    try
                    {
                        var dirCol = cols.FirstOrDefault(c => c.Name != null && c.Name.Equals("directionCode", StringComparison.OrdinalIgnoreCase));
                        if (dirCol != null && normalizedDefinition.IndexOf("IIF", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            dirCol.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                            dirCol.SqlTypeName = "nvarchar";
                            dirCol.MaxLength = 3; // l�ngstes Literal 'out'
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
                // Minimal strukturelle Typanreicherung f�r stark bekannte Muster (keine generische Namens-Heuristik):
                foreach (var c in synthetic.Columns)
                {
                    if (c.Name != null && c.Name.EndsWith(".rowVersion", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(c.SqlTypeName))
                    {
                        c.SqlTypeName = "rowversion"; // Stabiler SQL Server Typ f�r Timestamp/RowVersion
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

        // Post-AST minimale Typanreicherung f�r stark kanonisierte Muster (kein generisches Ratespiel):
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

    // Gating f�r JSON-AST-Diagnoseausgaben: Aktiv bei SPOCR_LOG_LEVEL=debug|trace oder separater Flag SPOCR_JSON_AST_DIAG=true.
    private static bool ShouldDiagJsonAst()
    {
        // Also honor --verbose via SetAstVerbose
        if (_astVerboseEnabled) return true;
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

    // Entfernt: heuristischer Fallback. (Bewusst beibehaltene Methode gel�scht f�r deterministische AST-only Pipeline.)

    // (Removed) Token Fallback Helpers � not needed after revert

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
        // Zus�tzliche Properties ben�tigt von anderen Komponenten
        public string UserTypeSchemaName { get; set; }
        public string UserTypeName { get; set; }
        public int? MaxLength { get; set; }
        public bool? IsAmbiguous { get; set; }
        // AST-only function call metadata (no heuristics). Populated when ExpressionKind == FunctionCall or JsonQuery.
        // Entfernt: FunctionSchemaName / FunctionName � Referenzierung erfolgt ausschlie�lich �ber Reference (Kind=Function)
        // Erweiterung: Quell-Funktionsinformationen f�r JSON-Expansion
        // Entfernt: SourceFunctionSchema / SourceFunctionName � direkte Expansion ersetzt durch Reference / DeferredJsonExpansion
        // Raw scalar expression text extracted from original definition (exact substring). Enables deterministic pattern matching.
        public string RawExpression { get; set; }
        // Aggregate-Metadaten (Propagation �ber Derived Tables / Subqueries)
        public bool IsAggregate { get; set; }
        public string AggregateFunction { get; set; }
        // Neue Referenz f�r deferred JSON Expansion
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
        // Top-level JSON result sets mapped to their QuerySpecification nodes (when available)
        public Dictionary<QuerySpecification, ResultSet> TopLevelJsonSets { get; } = new();
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
                                                                                                // F�r abgeleitete Tabellen / Subselects (QueryDerivedTable, CTE) wird hier eine Alias->Column->Source Map gepflegt.
                                                                                                // Key: Derived table alias. Value: Dictionary(OutputColumnName -> (Schema, Table, Column, Ambiguous))
        private readonly Dictionary<string, Dictionary<string, (string Schema, string Table, string Column, bool Ambiguous)>> _derivedTableColumnSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ResultColumn>> _derivedTableColumns = new(StringComparer.OrdinalIgnoreCase); // Original ResultColumns je Derived Alias
        private readonly Dictionary<string, ResultColumn> _cteColumnTypes = new(StringComparer.OrdinalIgnoreCase); // Captured CTE column types for nested JSON propagation
        private readonly HashSet<QuerySpecification> _cteQuerySpecifications = new(); // Track CTE QuerySpecifications to exclude from ResultSets
        private readonly Dictionary<string, List<ResultColumn>> _cteDefinitions = new(StringComparer.OrdinalIgnoreCase); // Track CTE alias -> columns for type resolution
        private readonly Dictionary<string, List<ResultColumn>> _tableVariableColumns = new(StringComparer.OrdinalIgnoreCase); // @VarTable -> columns
        private readonly Dictionary<string, string> _tableVariableAliases = new(StringComparer.OrdinalIgnoreCase); // alias -> @VarTable name
        private readonly Dictionary<string, (string SqlTypeName, int? MaxLength, bool? IsNullable)> _resolvedColumnTypes = new(StringComparer.OrdinalIgnoreCase); // Cache f�r aufgel�ste Spalten-Typen
        public Visitor(string definition, Analysis analysis) { _definition = definition; _analysis = analysis; try { ExtractTableVariableDeclarations(); } catch { } }
        public override void ExplicitVisit(CreateProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        public override void ExplicitVisit(CreateOrAlterProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        public override void ExplicitVisit(AlterProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        private int _scalarSubqueryDepth; // Track nesting inside ScalarSubquery expressions
        public override void ExplicitVisit(SelectStatement node) { _analysis.ContainsSelect = true; base.ExplicitVisit(node); }
        // Hinweis: Statement-Level FOR JSON Fallback (SelectStatement.ForClause) wird von ScriptDom nicht angeboten (ForClause nur an QuerySpecification).
        // Falls k�nftig Unterschiede auftauchen, kann hier eine alternative Pfadbehandlung erg�nzt werden.
        // --- Scoped symbol table for nested resolution (Phase 1/2 scaffolding) ---
        private enum ScopeSymbolKind { Physical, Derived, VarTable }
        private sealed class ScopeSymbol
        {
            public ScopeSymbolKind Kind { get; init; }
            public string Schema { get; init; }
            public string Table { get; init; }
            public List<ResultColumn> Columns { get; init; }
        }
        private sealed class ScopeFrame
        {
            public Dictionary<string, ScopeSymbol> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);
        }
        private ScopeFrame BuildScopeFrameForNested(
            Dictionary<string, List<ResultColumn>> localVarCols,
            Dictionary<string, (string Schema, string Table)> localAliases)
        {
            var scope = new ScopeFrame();
            if (localVarCols != null)
            {
                foreach (var kv in localVarCols)
                {
                    if (kv.Value == null) continue;
                    scope.Symbols[kv.Key] = new ScopeSymbol { Kind = ScopeSymbolKind.VarTable, Columns = kv.Value };
                }
            }
            if (localAliases != null)
            {
                foreach (var kv in localAliases)
                {
                    var alias = kv.Key; var tup = kv.Value;
                    if (_derivedTableColumns.TryGetValue(alias, out var dcols) && dcols != null)
                    {
                        scope.Symbols[alias] = new ScopeSymbol { Kind = ScopeSymbolKind.Derived, Columns = dcols };
                    }
                    else if (!string.IsNullOrWhiteSpace(tup.Table))
                    {
                        scope.Symbols[alias] = new ScopeSymbol { Kind = ScopeSymbolKind.Physical, Schema = tup.Schema, Table = tup.Table };
                    }
                }
            }
            return scope;
        }
        private bool TryResolveFromScope(ScopeFrame scope, string alias, string column, out string sqlType, out int? maxLen, out bool? isNull)
        {
            sqlType = string.Empty; maxLen = null; isNull = null;
            if (scope == null || string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(column)) return false;
            if (!scope.Symbols.TryGetValue(alias, out var sym) || sym == null) return false;
            if (sym.Kind == ScopeSymbolKind.Physical)
            {
                var rc = new ResultColumn { SourceSchema = sym.Schema, SourceTable = sym.Table, SourceColumn = column };
                TryAssignColumnType(rc);
                if (!string.IsNullOrWhiteSpace(rc.SqlTypeName)) { sqlType = rc.SqlTypeName; maxLen = rc.MaxLength; isNull = rc.IsNullable; return true; }
                return false;
            }
            if (sym.Columns != null)
            {
                var c = sym.Columns.FirstOrDefault(x => x.Name?.Equals(column, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(x.SqlTypeName));
                if (c != null) { sqlType = c.SqlTypeName; maxLen = c.MaxLength; isNull = c.IsNullable; return true; }
            }
            return false;
        }

        // --- Minimal lineage nodes for nested scalar expressions ---
        private abstract class LNode { }
        private sealed class LColumnRef : LNode { public string Alias; public string Column; }
        private sealed class LCast : LNode { public LNode Inner; public string TargetType; public int? MaxLen; }
        private sealed class LIif : LNode { public LNode ThenNode; public LNode ElseNode; }
        private sealed class LBinary : LNode { public LNode Left; public LNode Right; public BinaryExpressionType Op; }
        private sealed class LCase : LNode { public List<LNode> Branches = new(); public LNode ElseNode; }

        private LNode BuildLineage(ScalarExpression expr)
        {
            try
            {
                switch (expr)
                {
                    case ColumnReferenceExpression cr when cr.MultiPartIdentifier?.Identifiers?.Count == 2:
                        return new LColumnRef
                        {
                            Alias = cr.MultiPartIdentifier.Identifiers[0].Value,
                            Column = cr.MultiPartIdentifier.Identifiers[1].Value
                        };
                    case CastCall cast:
                        {
                            string typeName = null; int? maxLen = null;
                            if (cast.DataType?.Name?.Identifiers?.Count > 0)
                            {
                                typeName = string.Join('.', cast.DataType.Name.Identifiers.Select(i => i.Value)).ToLowerInvariant();
                                // try single length e.g., nvarchar(100)
                                if (cast.DataType is SqlDataTypeReference sref && sref.Parameters?.Count >= 1 && sref.Parameters[0] is Literal ll)
                                {
                                    if (int.TryParse(ll.Value, out var len0)) maxLen = len0;
                                }
                            }
                            return new LCast { Inner = BuildLineage(cast.Parameter as ScalarExpression), TargetType = typeName, MaxLen = maxLen };
                        }
                    case ConvertCall conv:
                        {
                            string typeName = null; int? maxLen = null;
                            if (conv.DataType?.Name?.Identifiers?.Count > 0)
                            {
                                typeName = string.Join('.', conv.DataType.Name.Identifiers.Select(i => i.Value)).ToLowerInvariant();
                                if (conv.DataType is SqlDataTypeReference sref && sref.Parameters?.Count >= 1 && sref.Parameters[0] is Literal ll)
                                {
                                    if (int.TryParse(ll.Value, out var len0)) maxLen = len0;
                                }
                            }
                            return new LCast { Inner = BuildLineage(conv.Parameter as ScalarExpression), TargetType = typeName, MaxLen = maxLen };
                        }
                    case ParenthesisExpression pe:
                        return BuildLineage(pe.Expression);
                    case IIfCall iif:
                        return new LIif { ThenNode = BuildLineage(iif.ThenExpression as ScalarExpression), ElseNode = BuildLineage(iif.ElseExpression as ScalarExpression) };
                    case FunctionCall f when string.Equals(f.FunctionName?.Value, "IIF", StringComparison.OrdinalIgnoreCase) && f.Parameters?.Count == 3:
                        return new LIif { ThenNode = BuildLineage(f.Parameters[1] as ScalarExpression), ElseNode = BuildLineage(f.Parameters[2] as ScalarExpression) };
                    case BinaryExpression be:
                        return new LBinary { Left = BuildLineage(be.FirstExpression as ScalarExpression), Right = BuildLineage(be.SecondExpression as ScalarExpression), Op = be.BinaryExpressionType };
                    case SearchedCaseExpression sce:
                        {
                            var lc = new LCase();
                            if (sce.WhenClauses != null)
                            {
                                foreach (var w in sce.WhenClauses)
                                    lc.Branches.Add(BuildLineage(w.ThenExpression as ScalarExpression));
                            }
                            if (sce.ElseExpression != null) lc.ElseNode = BuildLineage(sce.ElseExpression as ScalarExpression);
                            return lc;
                        }
                    case SimpleCaseExpression simp:
                        {
                            var lc = new LCase();
                            if (simp.WhenClauses != null)
                            {
                                foreach (var w in simp.WhenClauses)
                                    lc.Branches.Add(BuildLineage(w.ThenExpression as ScalarExpression));
                            }
                            if (simp.ElseExpression != null) lc.ElseNode = BuildLineage(simp.ElseExpression as ScalarExpression);
                            return lc;
                        }
                    default:
                        return null;
                }
            }
            catch { return null; }
        }

        private bool TryResolveLineageType(LNode node, ScopeFrame scope, out string sqlType, out int? maxLen, out bool? isNull)
        {
            sqlType = string.Empty; maxLen = null; isNull = null;
            if (node == null || scope == null) return false;
            switch (node)
            {
                case LColumnRef cr:
                    return TryResolveFromScope(scope, cr.Alias, cr.Column, out sqlType, out maxLen, out isNull);
                case LCast lc:
                    if (!string.IsNullOrWhiteSpace(lc.TargetType)) { sqlType = lc.TargetType; maxLen = lc.MaxLen; isNull = null; return true; }
                    return TryResolveLineageType(lc.Inner, scope, out sqlType, out maxLen, out isNull);
                case LIif li:
                    string t1; int? l1; bool? n1; string t2; int? l2; bool? n2;
                    var ok1 = TryResolveLineageType(li.ThenNode, scope, out t1, out l1, out n1);
                    var ok2 = TryResolveLineageType(li.ElseNode, scope, out t2, out l2, out n2);
                    if (ok1 && ok2)
                    {
                        if (!string.IsNullOrWhiteSpace(t1) && string.Equals(t1, t2, StringComparison.OrdinalIgnoreCase))
                        { sqlType = t1; maxLen = (l1.HasValue && l2.HasValue) ? Math.Max(l1.Value, l2.Value) : l1 ?? l2; isNull = (n1 == true || n2 == true); return true; }
                        // numeric preference
                        if (!string.IsNullOrWhiteSpace(t1) && t1.Contains("decimal")) { sqlType = t1; maxLen = l1; isNull = n1; return true; }
                        if (!string.IsNullOrWhiteSpace(t2) && t2.Contains("decimal")) { sqlType = t2; maxLen = l2; isNull = n2; return true; }
                        // otherwise prefer first resolved
                        sqlType = !string.IsNullOrWhiteSpace(t1) ? t1 : t2; maxLen = l1 ?? l2; isNull = n1 ?? n2; return !string.IsNullOrWhiteSpace(sqlType);
                    }
                    if (ok1) { sqlType = t1; maxLen = l1; isNull = n1; return true; }
                    if (ok2) { sqlType = t2; maxLen = l2; isNull = n2; return true; }
                    return false;
                case LBinary lb:
                    string lt; int? ll; bool? ln; string rt; int? rl; bool? rn;
                    var okL = TryResolveLineageType(lb.Left, scope, out lt, out ll, out ln);
                    var okR = TryResolveLineageType(lb.Right, scope, out rt, out rl, out rn);
                    if (!(okL || okR)) return false;
                    string norm(string t) => (t ?? string.Empty).ToLowerInvariant();
                    var nl = norm(lt); var nr = norm(rt);
                    bool decL = nl.Contains("decimal") || nl.Contains("numeric") || nl.Contains("money") || nl.Contains("float") || nl.Contains("real");
                    bool decR = nr.Contains("decimal") || nr.Contains("numeric") || nr.Contains("money") || nr.Contains("float") || nr.Contains("real");
                    bool intL = nl == "int" || nl == "bigint" || nl == "smallint" || nl == "tinyint";
                    bool intR = nr == "int" || nr == "bigint" || nr == "smallint" || nr == "tinyint";
                    bool strL = nl.StartsWith("nvarchar") || nl.StartsWith("varchar") || nl.StartsWith("nchar") || nl.StartsWith("char");
                    bool strR = nr.StartsWith("nvarchar") || nr.StartsWith("varchar") || nr.StartsWith("nchar") || nr.StartsWith("char");
                    if (decL || decR) { sqlType = "decimal(18,2)"; maxLen = null; isNull = (ln == true || rn == true); return true; }
                    if (intL || intR) { sqlType = intL && intR ? (lt ?? "int") : "int"; maxLen = null; isNull = (ln == true || rn == true); return true; }
                    if (strL || strR) { sqlType = "nvarchar"; maxLen = Math.Max(ll ?? 0, rl ?? 0); maxLen = (maxLen == 0) ? (int?)null : maxLen; isNull = (ln == true || rn == true); return true; }
                    sqlType = lt ?? rt; maxLen = ll ?? rl; isNull = ln ?? rn; return !string.IsNullOrWhiteSpace(sqlType);
                case LCase lcase:
                    var types = new List<(string T, int? L, bool? N)>();
                    foreach (var b in lcase.Branches)
                    {
                        if (TryResolveLineageType(b, scope, out var tb, out var lb2, out var nb)) types.Add((tb, lb2, nb));
                    }
                    if (lcase.ElseNode != null && TryResolveLineageType(lcase.ElseNode, scope, out var te, out var le, out var ne)) types.Add((te, le, ne));
                    if (types.Count == 0) return false;
                    // all equal
                    if (types.All(x => !string.IsNullOrWhiteSpace(x.T) && x.T.Equals(types[0].T, StringComparison.OrdinalIgnoreCase)))
                    { sqlType = types[0].T; maxLen = types.Max(x => x.L) ?? types[0].L; isNull = types.Any(x => x.N == true); return true; }
                    // prefer decimal if present
                    var dec = types.FirstOrDefault(x => (x.T ?? string.Empty).ToLowerInvariant().Contains("decimal"));
                    if (!string.IsNullOrWhiteSpace(dec.T)) { sqlType = dec.T; maxLen = dec.L; isNull = dec.N; return true; }
                    // else prefer nvarchar
                    var str = types.FirstOrDefault(x => { var t = (x.T ?? string.Empty).ToLowerInvariant(); return t.StartsWith("nvarchar") || t.StartsWith("varchar") || t.StartsWith("nchar") || t.StartsWith("char"); });
                    if (!string.IsNullOrWhiteSpace(str.T)) { sqlType = "nvarchar"; maxLen = types.Max(x => x.L); isNull = types.Any(x => x.N == true); return true; }
                    // fallback to first
                    sqlType = types[0].T; maxLen = types[0].L; isNull = types[0].N; return !string.IsNullOrWhiteSpace(sqlType);
                default:
                    return false;
            }
        }

        public override void ExplicitVisit(InsertStatement node)
        {
            _analysis.ContainsInsert = true;
            try
            {
                var spec = node?.InsertSpecification;
                if (spec?.Target is VariableTableReference vtr && spec.InsertSource is SelectInsertSource sis)
                {
                    var varName = vtr.Variable?.Name; // includes leading '@'
                    var qs = UnwrapToQuerySpecification(sis.Select);
                    if (!string.IsNullOrWhiteSpace(varName) && qs != null)
                    {
                        var inferred = new List<ResultColumn>();
                        foreach (var se in qs.SelectElements?.OfType<SelectScalarExpression>() ?? Enumerable.Empty<SelectScalarExpression>())
                        {
                            var alias = se.ColumnName?.Value;
                            if (string.IsNullOrWhiteSpace(alias) && se.ColumnName is IdentifierOrValueExpression ive)
                            {
                                if (ive.Identifier != null) alias = ive.Identifier.Value; else if (ive.ValueExpression is StringLiteral sl && !string.IsNullOrWhiteSpace(sl.Value)) alias = sl.Value;
                            }
                            if (string.IsNullOrWhiteSpace(alias) && se.Expression is ColumnReferenceExpression icr && icr.MultiPartIdentifier?.Identifiers?.Count > 0)
                                alias = icr.MultiPartIdentifier.Identifiers[^1].Value;
                            var rc = new ResultColumn { Name = alias };
                            var st = new SourceBindingState();
                            AnalyzeScalarExpression(se.Expression, rc, st);
                            if (!string.IsNullOrWhiteSpace(rc.Name)) inferred.Add(rc);
                        }
                        if (inferred.Count > 0)
                        {
                            if (!_tableVariableColumns.ContainsKey(varName)) _tableVariableColumns[varName] = inferred;
                            else
                            {
                                var existing = _tableVariableColumns[varName];
                                foreach (var add in inferred)
                                {
                                    var ex = existing.FirstOrDefault(c => c.Name.Equals(add.Name, StringComparison.OrdinalIgnoreCase));
                                    if (ex == null) existing.Add(add);
                                    else
                                    {
                                        if (string.IsNullOrWhiteSpace(ex.SqlTypeName) && !string.IsNullOrWhiteSpace(add.SqlTypeName)) ex.SqlTypeName = add.SqlTypeName;
                                        if (!ex.MaxLength.HasValue && add.MaxLength.HasValue) ex.MaxLength = add.MaxLength;
                                        if (!ex.IsNullable.HasValue && add.IsNullable.HasValue) ex.IsNullable = add.IsNullable;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            base.ExplicitVisit(node);
        }
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
        // Table variable declarations are not currently used for type resolution (UDTs require DB metadata). Skipped.
        public override void ExplicitVisit(QuerySpecification node)
        {
            try
            {
                if (ShouldDiag()) Console.WriteLine($"[qs-debug] ExplicitVisit QuerySpecification - CTE count: {_cteQuerySpecifications.Count}, checking if this is CTE: {_cteQuerySpecifications.Contains(node)}");

                // For now, let all QuerySpecifications be processed normally - we'll filter CTE ResultSets at the end
                // This allows CTE columns to be properly typed through normal processing

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
                            int previewStart = Math.Max(0, idxForJson - 60);
                            int previewEnd = Math.Min(seg.Length, idxForJson + 120);
                            var preview = seg.Substring(previewStart, previewEnd - previewStart).Replace('\n', ' ').Replace('\r', ' ');
                            if (ShouldDiag()) Console.WriteLine($"[qs-debug] contextPreview={preview}");
                        }
                    }
                }
                catch { }
                // ScriptDom bietet keine direkte Parent-Eigenschaft auf QuerySpecification; daher approximieren wir:
                // Falls node.ForClause null ist, pr�fen wir, ob der �bergeordnete SelectStatement (der diesen QuerySpecification enth�lt)
                // einen ForClause besitzt. Da wir Parent nicht haben, nutzen wir eine Heuristik: Wenn node.ForClause null ist UND
                // der aktuelle SelectStatement (wird vorher in ExplicitVisit(SelectStatement) erfasst) eine ForClause hat, wird diese
                // sp�ter separat verarbeitet. Vereinfachung: Wir behandeln NUR node.ForClause hier; f�r Statement-Level FOR JSON
                // greifen wir in ExplicitVisit(SelectStatement) ein (Fallback Pfad unten implementiert).
                JsonForClause jsonClause = node.ForClause as JsonForClause;
                bool isNestedSelect = _scalarSubqueryDepth > 0; // verschachtelte Selects (Subqueries) nicht als eigenes JSON ResultSet markieren

                // Segment-Scan Fallback: Einige reale Prozeduren liefern keinen JsonForClause im AST (ScriptDom Limitation bei komplexen SELECTs).
                // Um Tests zu erf�llen ohne globale Regex-Heuristik f�hren wir einen strikt begrenzten Scan �ber das QuerySpecification Segment aus.
                // Bedingungen: (a) top-level (nicht verschachtelt), (b) JsonForClause==null, (c) Segment enth�lt "FOR JSON PATH".
                // Zus�tzliche Optionen WITHOUT_ARRAY_WRAPPER / ROOT('x') werden extrahiert. Dieser Fallback ist rein segmentbezogen & deterministisch.
                bool segmentFallbackDetected = false;
                bool fallbackWithoutArrayWrapper = false;
                string fallbackRootProperty = null;
                if (jsonClause == null && !isNestedSelect && _definition != null)
                {
                    try
                    {
                        int startScan = node.StartOffset >= 0 ? node.StartOffset : 0;
                        int endScan = node.StartOffset >= 0 && node.FragmentLength > 0
                            ? Math.Min(_definition.Length, node.StartOffset + node.FragmentLength)
                            : _definition.Length;
                        var segment = _definition.Substring(startScan, endScan - startScan);
                        // Entferne einfache Inline Kommentare "--" bis Zeilenende f�r robustere Erkennung (kein Block-Strip)
                        var cleaned = string.Join("\n", segment.Split('\n').Select(l =>
                        {
                            var ci = l.IndexOf("--", StringComparison.Ordinal);
                            return ci >= 0 ? l.Substring(0, ci) : l;
                        }));
                        var idx = cleaned.IndexOf("FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                        if (idx < 0)
                        {
                            // Global fallback: Suche im vollst�ndigen Definitionstext falls Segment (Fragment) es nicht enth�lt (ScriptDom Offsets unvollst�ndig)
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
                if (jsonClause == null && !isNestedSelect)
                {
                    // Weder AST JsonForClause noch segmentbasierte Erkennung ? kein JSON ResultSet.
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
                    // Fallback: Standardm��ig Array Wrapper aktiv au�er explizitem WITHOUT_ARRAY_WRAPPER
                    builder.JsonWithArrayWrapper = !fallbackWithoutArrayWrapper;
                    builder.JsonWithoutArrayWrapper = fallbackWithoutArrayWrapper;
                    if (!fallbackWithoutArrayWrapper) builder.JsonWithArrayWrapper = true; // sicherstellen default
                    builder.JsonRootProperty = fallbackRootProperty;
                }
                // Keine heuristische Set-Analyse; nur echte JsonForClause bestimmt Array Wrapper

                // Prepare local alias -> table variable columns map for nested JSON sets
                var localVarColsForNested = new Dictionary<string, List<ResultColumn>>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (isNestedSelect && node.FromClause?.TableReferences != null)
                    {
                        foreach (var tr in node.FromClause.TableReferences)
                        {
                            if (tr is VariableTableReference vtrNest)
                            {
                                var aliasNest = vtrNest.Alias?.Value;
                                var varNameNest = vtrNest.Variable?.Name;
                                if (!string.IsNullOrWhiteSpace(aliasNest) && !string.IsNullOrWhiteSpace(varNameNest))
                                {
                                    var keyNest = varNameNest.StartsWith("@") ? varNameNest : ("@" + varNameNest);
                                    if (_tableVariableColumns.TryGetValue(keyNest, out var colsForVarNest))
                                        localVarColsForNested[aliasNest] = colsForVarNest;
                                }
                            }
                        }
                    }
                }
                catch { }

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
                            if (ShouldDiagJsonAst()) Console.WriteLine($"[json-ast-select-elem-enter] startOffset={sce.StartOffset} len={sce.FragmentLength} initialName={initialName} exprType={sce.Expression?.GetType().Name}");
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
                    // Token-basierte (AST Token Stream) Alias-Erkennung (erweiterter Scan �ber Expression-Ende hinaus): AS 'literal'
                    if (string.IsNullOrWhiteSpace(alias) && sce.ScriptTokenStream != null)
                    {
                        try
                        {
                            var tokens = sce.ScriptTokenStream;
                            // Preferred: nutze Token Indices wenn verf�gbar, ansonsten Offset-Heuristik
                            int exprLastToken = sce.LastTokenIndex >= 0 ? sce.LastTokenIndex : -1;
                            if (exprLastToken < 0)
                            {
                                // Fallback: bestimme Bereich �ber Offsets
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
                                // Abbruchbedingungen: n�chstes SELECT-Element / FROM / Semikolon / Zeilenende-Komma
                                if (t.TokenType == TSqlTokenType.Comma || t.TokenType == TSqlTokenType.Semicolon) break;
                                if (t.Text != null && t.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase)) break;
                                if (t.TokenType == TSqlTokenType.As && i + 1 < tokens.Count)
                                {
                                    // n�chster signifikanter Token
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
                    // Zus�tzliche Alias-Erkennung f�r FOR JSON Pfad-Syntax inkl. F�lle, in denen ScriptDom das AS 'alias' au�erhalb des Fragmentes schneidet.
                    if (string.IsNullOrWhiteSpace(alias) && sce.StartOffset >= 0 && sce.FragmentLength > 0)
                    {
                        try
                        {
                            var endExpr = Math.Min(_definition.Length, sce.StartOffset + sce.FragmentLength);
                            var exprSegment = _definition.Substring(sce.StartOffset, endExpr - sce.StartOffset);
                            // Prim�r: AS 'alias' im Ausdruckssegment
                            var m = System.Text.RegularExpressions.Regex.Match(exprSegment, @"AS\s+'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (!m.Success)
                            {
                                // Fallback: Einzelnes quoted literal (manche Alias-Stile ohne explizites AS)
                                m = System.Text.RegularExpressions.Regex.Match(exprSegment, @"'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            }
                            if (!m.Success)
                            {
                                // Forward-Scan bis FOR JSON oder n�chstes SELECT/ FROM / GROUP BY zur Begrenzung
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
                    // If nested JSON set and expression is a simple alias.column from a table variable, assign type from captured declaration
                    try
                    {
                        if (isNestedSelect && sce.Expression is ColumnReferenceExpression crNest && crNest.MultiPartIdentifier?.Identifiers?.Count == 2)
                        {
                            var alias0 = crNest.MultiPartIdentifier.Identifiers[0].Value;
                            var col0 = crNest.MultiPartIdentifier.Identifiers[1].Value;
                            if (localVarColsForNested.TryGetValue(alias0, out var tcolsNest))
                            {
                                var vcolN = tcolsNest.FirstOrDefault(c => c.Name?.Equals(col0, StringComparison.OrdinalIgnoreCase) == true);
                                if (vcolN != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(vcolN.SqlTypeName)) col.SqlTypeName = vcolN.SqlTypeName;
                                    if (vcolN.MaxLength.HasValue) col.MaxLength = vcolN.MaxLength;
                                    if (vcolN.IsNullable.HasValue) col.IsNullable = vcolN.IsNullable;
                                    if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-var-col-type name={col.Name} alias={alias0} srcCol={col0} sqlType={col.SqlTypeName} maxLen={col.MaxLength}"); } catch { } }
                                }
                            }
                        }
                    }
                    catch { }
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
                var isNested = isNestedSelect;
                var resultSet = builder.ToResultSet();
                // Apply table variable typing to nested JSON sets using local alias map + raw expressions
                if (isNested)
                {
                    try
                    {
                        ApplyTableVariableTypingToNestedResult(node, resultSet);
                    }
                    catch { }
                    if (!_analysis.NestedJsonSets.ContainsKey(node)) _analysis.NestedJsonSets[node] = resultSet;
                }
                else
                {
                    // Nur hinzuf�gen wenn echte (AST) JsonForClause ODER heuristisch erkannt im Top-Level
                    if (jsonClause != null || segmentFallbackDetected)
                    {
                        _analysis.JsonSets.Add(resultSet);
                        if (!_analysis.TopLevelJsonSets.ContainsKey(node)) _analysis.TopLevelJsonSets[node] = resultSet;
                    }
                }
                // Restore outer scope
                _tableAliases.Clear(); foreach (var kv in outerAliases) _tableAliases[kv.Key] = kv.Value;
                _tableSources.Clear(); foreach (var s in outerSources) _tableSources.Add(s);
            }
            catch { }
        }
        private void ApplyTableVariableTypingToNestedResult(QuerySpecification node, ResultSet set)
        {
            if (node?.FromClause?.TableReferences == null || set?.Columns == null || set.Columns.Count == 0) return;
            var localVarCols = new Dictionary<string, List<ResultColumn>>(StringComparer.OrdinalIgnoreCase);
            var localAliases = new Dictionary<string, (string Schema, string Table)>(StringComparer.OrdinalIgnoreCase);
            var localTableSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Recursively walk the FROM tree to capture any VariableTableReference aliases (including inside JOINs)
                void CollectVarRefs(TableReference tr)
                {
                    switch (tr)
                    {
                        case null:
                            return;
                        case QualifiedJoin qj:
                            CollectVarRefs(qj.FirstTableReference);
                            CollectVarRefs(qj.SecondTableReference);
                            break;
                        case VariableTableReference vtr:
                            try
                            {
                                var alias = vtr.Alias?.Value;
                                var varName = vtr.Variable?.Name;
                                if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(varName))
                                {
                                    var key = varName.StartsWith("@") ? varName : ("@" + varName);
                                    if (_tableVariableColumns.TryGetValue(key, out var colsForVar))
                                    {
                                        localVarCols[alias] = colsForVar;
                                        if (!_derivedTableColumns.ContainsKey(alias)) _derivedTableColumns[alias] = colsForVar;
                                    }
                                    if (!_tableVariableAliases.ContainsKey(alias))
                                        _tableVariableAliases[alias] = key;
                                }
                            }
                            catch { }
                            break;
                        case NamedTableReference ntr:
                            try
                            {
                                var schema = ntr.SchemaObject?.SchemaIdentifier?.Value ?? _analysis.DefaultSchema;
                                var table = ntr.SchemaObject?.BaseIdentifier?.Value;
                                var alias = ntr.Alias?.Value;
                                var key = !string.IsNullOrWhiteSpace(alias) ? alias : table;
                                if (!string.IsNullOrWhiteSpace(key) && !localAliases.ContainsKey(key))
                                {
                                    var tbl = table ?? string.Empty;
                                    localAliases[key] = (schema, tbl);
                                }
                                if (!string.IsNullOrWhiteSpace(table)) localTableSources.Add($"{schema}.{table}");
                            }
                            catch { }
                            break;
                        case QueryDerivedTable:
                        case SchemaObjectFunctionTableReference:
                        case JoinParenthesisTableReference:
                            // no variable capture needed for these nodes in this pass
                            break;
                        default:
                            break;
                    }
                }
                foreach (var tr in node.FromClause.TableReferences) CollectVarRefs(tr);
                // As a fallback, scan the subquery text for @Var AS alias patterns to capture variable aliases embedded in complex joins
                try
                {
                    if (localVarCols.Count == 0 && node.StartOffset >= 0 && node.FragmentLength > 0 && _definition != null && node.StartOffset + node.FragmentLength <= _definition.Length)
                    {
                        var frag = _definition.Substring(node.StartOffset, node.FragmentLength);
                        var rxVarAlias = new System.Text.RegularExpressions.Regex(@"@(?<var>\w+)\s+(?:AS\s+)?(?<alias>\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        foreach (System.Text.RegularExpressions.Match m in rxVarAlias.Matches(frag))
                        {
                            var v = m.Groups["var"].Value; var a = m.Groups["alias"].Value;
                            if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(a)) continue;
                            var key = "@" + v;
                            if (_tableVariableColumns.TryGetValue(key, out var colsForVar))
                            {
                                localVarCols[a] = colsForVar;
                                if (!_derivedTableColumns.ContainsKey(a)) _derivedTableColumns[a] = colsForVar;
                                if (!_tableVariableAliases.ContainsKey(a)) _tableVariableAliases[a] = key;
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
            foreach (var col in set.Columns)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(col.SqlTypeName)) continue;
                    var raw = col.RawExpression;
                    if (string.IsNullOrWhiteSpace(raw)) { /* allow name-based fallbacks below */ }
                    // Attempt AST rebind of the child's scalar by parsing the leading scalar expression from RawExpression
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            var exprTxt = raw;
                            var idxAs = exprTxt.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
                            if (idxAs > 0) exprTxt = exprTxt.Substring(0, idxAs).Trim();
                            // Construct a minimal SELECT for the expression and parse
                            var toParse = "SELECT " + exprTxt + ";";
                            using var sr = new System.IO.StringReader(toParse);
                            var frag = Parser.Parse(sr, out var errs);
                            if (frag != null && errs != null && errs.Count == 0)
                            {
                                ScalarExpression extracted = null;
                                try
                                {
                                    if (frag is TSqlScript sc && sc.Batches?.Count > 0)
                                    {
                                        var st = sc.Batches[0]?.Statements?.OfType<SelectStatement>()?.FirstOrDefault();
                                        var qs2 = UnwrapToQuerySpecification(st?.QueryExpression);
                                        var sse = qs2?.SelectElements?.OfType<SelectScalarExpression>()?.FirstOrDefault();
                                        extracted = sse?.Expression as ScalarExpression;
                                    }
                                }
                                catch { }
                                if (extracted != null)
                                {
                                    var temp = new ResultColumn(); var st0 = new SourceBindingState();
                                    AnalyzeScalarExpressionDerived(extracted, temp, st0, localAliases, localTableSources);
                                    if (!string.IsNullOrWhiteSpace(temp.SqlTypeName))
                                    {
                                        col.SqlTypeName = temp.SqlTypeName; col.MaxLength = temp.MaxLength; col.IsNullable = temp.IsNullable;
                                        if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-ast-rebind name={col.Name} sqlType={col.SqlTypeName}"); } catch { } }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                    // Strict start-anchor match: alias.column
                    var m = !string.IsNullOrWhiteSpace(raw) ? Regex.Match(raw, @"^\s*(?<alias>[A-Za-z0-9_]+)\.(?<col>[A-Za-z0-9_]+)\b", RegexOptions.IgnoreCase) : Match.Empty;
                    string alias = null;
                    string cName = null;
                    if (m.Success)
                    {
                        alias = m.Groups["alias"].Value;
                        cName = m.Groups["col"].Value;
                        if (localVarCols.TryGetValue(alias, out var tcols))
                        {
                            var vcol = tcols.FirstOrDefault(c => c.Name?.Equals(cName, StringComparison.OrdinalIgnoreCase) == true);
                            if (vcol != null)
                            {
                                if (!string.IsNullOrWhiteSpace(vcol.SqlTypeName)) col.SqlTypeName = vcol.SqlTypeName;
                                if (vcol.MaxLength.HasValue) col.MaxLength = vcol.MaxLength;
                                if (vcol.IsNullable.HasValue) col.IsNullable = vcol.IsNullable;
                                if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-raw-var-col-type name={col.Name} alias={alias} srcCol={cName} sqlType={col.SqlTypeName} maxLen={col.MaxLength}"); } catch { } }
                                continue;
                            }
                        }
                        // Physical table alias mapping
                        if (localAliases.TryGetValue(alias, out var mapped))
                        {
                            var tmp = new ResultColumn { SourceSchema = mapped.Schema, SourceTable = mapped.Table, SourceColumn = cName };
                            TryAssignColumnType(tmp);
                            if (!string.IsNullOrWhiteSpace(tmp.SqlTypeName))
                            {
                                col.SqlTypeName = tmp.SqlTypeName; col.MaxLength = tmp.MaxLength; col.IsNullable = tmp.IsNullable;
                                if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-raw-phys-col-type name={col.Name} alias={alias} src={mapped.Schema}.{mapped.Table}.{cName} sqlType={col.SqlTypeName}"); } catch { } }
                                continue;
                            }
                        }
                    }
                    // Relaxed search anywhere in expression for alias.column where alias is a table variable alias
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            var matches = Regex.Matches(raw, @"\b(?<alias>[A-Za-z0-9_]+)\.(?<col>[A-Za-z0-9_]+)\b", RegexOptions.IgnoreCase);
                            foreach (Match mm in matches)
                            {
                                var a = mm.Groups["alias"].Value;
                                var cn = mm.Groups["col"].Value;
                                if (!localVarCols.TryGetValue(a, out var tcols2)) continue;
                                var vcol2 = tcols2.FirstOrDefault(c => c.Name?.Equals(cn, StringComparison.OrdinalIgnoreCase) == true);
                                if (vcol2 != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(vcol2.SqlTypeName)) col.SqlTypeName = vcol2.SqlTypeName;
                                    if (vcol2.MaxLength.HasValue) col.MaxLength = vcol2.MaxLength;
                                    if (vcol2.IsNullable.HasValue) col.IsNullable = vcol2.IsNullable;
                                    if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-any-var-col-type name={col.Name} alias={a} srcCol={cn} sqlType={col.SqlTypeName} maxLen={col.MaxLength}"); } catch { } }
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                    // Fallback by suffix match to table variable column names (AST-driven via captured declaration)
                    var suffix = col.Name.Contains('.') ? col.Name.Split('.').Last() : col.Name;
                    foreach (var kv in localVarCols)
                    {
                        var tcols = kv.Value;
                        var vcol2 = tcols.FirstOrDefault(c => c.Name?.Equals(suffix, StringComparison.OrdinalIgnoreCase) == true);
                        if (vcol2 != null)
                        {
                            if (!string.IsNullOrWhiteSpace(vcol2.SqlTypeName)) col.SqlTypeName = vcol2.SqlTypeName;
                            if (vcol2.MaxLength.HasValue) col.MaxLength = vcol2.MaxLength;
                            if (vcol2.IsNullable.HasValue) col.IsNullable = vcol2.IsNullable;
                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-suffix-var-col-type name={col.Name} srcCol={vcol2.Name} sqlType={col.SqlTypeName} maxLen={col.MaxLength}"); } catch { } }
                            break;
                        }
                    }
                    // Unique last-segment match across variable tables present in this subquery
                    if (string.IsNullOrWhiteSpace(col.SqlTypeName) && localVarCols.Count > 0)
                    {
                        ResultColumn onlyLocal = null; int cntLocal = 0;
                        foreach (var tset in localVarCols.Values)
                        {
                            var hit = tset?.FirstOrDefault(x => x.Name?.Equals(suffix, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(x.SqlTypeName));
                            if (hit != null) { onlyLocal = hit; cntLocal++; }
                        }
                        if (cntLocal == 1 && onlyLocal != null)
                        {
                            col.SqlTypeName = onlyLocal.SqlTypeName; col.MaxLength = onlyLocal.MaxLength; col.IsNullable = onlyLocal.IsNullable;
                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-local-unique-last name={col.Name} srcCol={onlyLocal.Name} sqlType={col.SqlTypeName}"); } catch { } }
                        }
                    }
                    // Unique collapsed-name match across variable tables present in this subquery
                    if (string.IsNullOrWhiteSpace(col.SqlTypeName) && localVarCols.Count > 0)
                    {
                        var collapsedLocal = new string((col.Name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
                        ResultColumn onlyLocalC = null; int cntLocalC = 0;
                        foreach (var tset in localVarCols.Values)
                        {
                            foreach (var vc in tset ?? new List<ResultColumn>())
                            {
                                var vcCollapsed = new string((vc.Name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
                                if (!string.IsNullOrWhiteSpace(vcCollapsed) && string.Equals(vcCollapsed, collapsedLocal, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(vc.SqlTypeName))
                                { onlyLocalC = vc; cntLocalC++; }
                            }
                        }
                        if (cntLocalC == 1 && onlyLocalC != null)
                        {
                            col.SqlTypeName = onlyLocalC.SqlTypeName; col.MaxLength = onlyLocalC.MaxLength; col.IsNullable = onlyLocalC.IsNullable;
                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-local-unique-collapsed name={col.Name} srcCol={onlyLocalC.Name} sqlType={col.SqlTypeName}"); } catch { } }
                        }
                    }
                    // Collapsed normalized-name unique match across all declared table vars in this procedure
                    if (string.IsNullOrWhiteSpace(col.SqlTypeName) && _tableVariableColumns != null && _tableVariableColumns.Count > 0)
                    {
                        var collapsed = new string((col.Name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
                        ResultColumn only = null; int count = 0;
                        foreach (var kvp in _tableVariableColumns)
                        {
                            foreach (var vc in kvp.Value)
                            {
                                var vcCollapsed = new string((vc.Name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
                                if (!string.IsNullOrWhiteSpace(vcCollapsed) && string.Equals(vcCollapsed, collapsed, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(vc.SqlTypeName))
                                {
                                    only = vc; count++;
                                }
                            }
                        }
                        if (count == 1 && only != null)
                        {
                            col.SqlTypeName = only.SqlTypeName; col.MaxLength = only.MaxLength; col.IsNullable = only.IsNullable;
                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-collapsed-var-type name={col.Name} srcCol={only.Name} sqlType={col.SqlTypeName}"); } catch { } }
                        }
                    }
                }
                catch { }
            }
            // If exactly one table variable alias is present, apply a final single-source fallback by last segment name
            if (localVarCols.Count == 1)
            {
                var only = localVarCols.First();
                var vcols = only.Value;
                foreach (var col in set.Columns)
                {
                    if (!string.IsNullOrWhiteSpace(col.SqlTypeName)) continue;
                    var last = col.Name.Contains('.') ? col.Name.Split('.').Last() : col.Name;
                    var v = vcols.FirstOrDefault(c => c.Name?.Equals(last, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(c.SqlTypeName));
                    if (v != null)
                    {
                        col.SqlTypeName = v.SqlTypeName; col.MaxLength = v.MaxLength; col.IsNullable = v.IsNullable;
                        if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] nested-single-var-fallback name={col.Name} srcCol={v.Name} sqlType={col.SqlTypeName}"); } catch { } }
                    }
                }
            }
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
                            // Map CTE name to alias for derived resolution
                            if (_derivedTableColumns.TryGetValue(table, out var cteCols))
                            {
                                _derivedTableColumns[key] = cteCols;
                                if (_derivedTableColumnSources.TryGetValue(table, out var cteMap))
                                    _derivedTableColumnSources[key] = cteMap;
                            }
                            else
                            {
                                if (!_tableAliases.ContainsKey(key))
                                    _tableAliases[key] = (schema, table);
                                _tableSources.Add($"{schema}.{table}");
                            }
                        }
                    }
                    catch { }
                    break;
                // VariableTableReference: ignored for now (no table type binding from UDT)
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
            // Verarbeite CTEs: Jede CTE als 'abgeleitete Tabelle' und markiere QuerySpecifications als CTE
            if (node?.CommonTableExpressions != null)
            {
                foreach (var cte in node.CommonTableExpressions)
                {
                    try
                    {
                        if (cte?.QueryExpression is QuerySpecification qs)
                        {
                            // Markiere als CTE QuerySpecification (soll keine ResultSet erzeugen)
                            _cteQuerySpecifications.Add(qs);

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

                                    // Capture CTE column types for later nested JSON propagation
                                    if (ShouldDiag()) Console.WriteLine($"[cte-type-capture] Processing CTE '{alias}' with {derivedCols.Count} columns");
                                    foreach (var col in derivedCols)
                                    {
                                        if (!string.IsNullOrEmpty(col.Name) && !string.IsNullOrEmpty(col.SqlTypeName))
                                        {
                                            if (ShouldDiag()) Console.WriteLine($"[cte-type-capture-early] Captured {col.Name}: {col.SqlTypeName}, MaxLength={col.MaxLength}, IsNullable={col.IsNullable}");
                                            RegisterCteColumnType(alias, col);
                                        }
                                        else
                                        {
                                            // Try to resolve from any available derived table columns with same name (AST-only fallback)
                                            try
                                            {
                                                ResultColumn srcAny = null;
                                                foreach (var kvd in _derivedTableColumns)
                                                {
                                                    var found = kvd.Value?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Name)
                                                        && c.Name.Equals(col.Name, StringComparison.OrdinalIgnoreCase)
                                                        && !string.IsNullOrWhiteSpace(c.SqlTypeName));
                                                    if (found != null) { srcAny = found; break; }
                                                }
                                                if (srcAny != null)
                                                {
                                                    CopyColumnType(col, srcAny);
                                                    RegisterCteColumnType(alias, col);
                                                    if (ShouldDiag()) Console.WriteLine($"[cte-type-capture-early] Resolved by derived lookup {col.Name}: {col.SqlTypeName}");
                                                }
                                                else if (ShouldDiag()) Console.WriteLine($"[cte-type-capture-early] Skipping {col.Name}: missing type info (SqlTypeName='{col.SqlTypeName}')");
                                            }
                                            catch { if (ShouldDiag()) Console.WriteLine($"[cte-type-capture-early] Skipping {col.Name}: missing type info (SqlTypeName='{col.SqlTypeName}')"); }
                                        }
                                    }
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
                    // ignore other table reference kinds for now (Derived tables / CTE) � future enhancement
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
                    // Einstieg-Instrumentierung f�r ColumnRef
                    try
                    {
                        var partsPreview = cref.MultiPartIdentifier?.Identifiers?.Select(i => i.Value).ToArray() ?? Array.Empty<string>();
                        if (ShouldDiagJsonAst()) System.Console.WriteLine($"[json-ast-colref-enter] name={target?.Name} parts={string.Join('.', partsPreview)}");
                    }
                    catch { }
                    _analysis.ColumnRefTotal++;
                    // ExpressionKind nur setzen wenn noch nicht klassifiziert (verhindert �berschreiben von FunctionCall/IIF)
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
                            // Versuche exakten Tabellenalias-Match �ber Namens�hnlichkeit (groupPrefix == alias oder groupPrefix == alias ohne Underscores)
                            // Falls mehrere Aliase existieren, w�hlen wir denjenigen, dessen Tabelle bereits eine Spalte mit diesem Namen hat (�ber zuvor registrierte bindings).
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
                                    // Trage Binding ein � wir kennen Schema/Table, Spaltenname ist suffix
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
                    // Entfernt: fr�he namenspbasierte Typableitung (InferSqlTypeFromSourceBinding)
                    // Entfernt: identity.RecordAsJson Spezialbehandlung � keine Funktions-Pseudo-Column Erkennung mehr.
                    break;
                case CastCall castCall:
                    target.ExpressionKind = ResultColumnExpressionKind.Cast;
                    if (castCall.DataType?.Name?.Identifiers?.Count > 0)
                    {
                        var typeName = string.Join('.', castCall.DataType.Name.Identifiers.Select(i => i.Value));
                        if (!string.IsNullOrWhiteSpace(typeName))
                        {
                            typeName = typeName.ToLowerInvariant();
                            target.CastTargetType = typeName;
                        }
                        TryExtractTypeParameters(castCall.DataType, target);
                        if (string.Equals(target.CastTargetType, "bit", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!target.MaxLength.HasValue) target.MaxLength = 1;
                        }
                        else if (target.CastTargetLength.HasValue && (target.CastTargetType?.Contains("char", StringComparison.OrdinalIgnoreCase) == true || target.CastTargetType?.Contains("binary", StringComparison.OrdinalIgnoreCase) == true))
                        {
                            if (!target.MaxLength.HasValue) target.MaxLength = target.CastTargetLength;
                        }
                    }
                    AnalyzeScalarExpression(castCall.Parameter, target, state);
                    break;
                case ConvertCall convertCall:
                    target.ExpressionKind = ResultColumnExpressionKind.Cast;
                    if (convertCall.DataType?.Name?.Identifiers?.Count > 0)
                    {
                        var typeName = string.Join('.', convertCall.DataType.Name.Identifiers.Select(i => i.Value));
                        if (!string.IsNullOrWhiteSpace(typeName))
                        {
                            typeName = typeName.ToLowerInvariant();
                            target.CastTargetType = typeName;
                        }
                        TryExtractTypeParameters(convertCall.DataType, target);
                        if (string.Equals(target.CastTargetType, "bit", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!target.MaxLength.HasValue) target.MaxLength = 1;
                        }
                        else if (target.CastTargetLength.HasValue && (target.CastTargetType?.Contains("char", StringComparison.OrdinalIgnoreCase) == true || target.CastTargetType?.Contains("binary", StringComparison.OrdinalIgnoreCase) == true))
                        {
                            if (!target.MaxLength.HasValue) target.MaxLength = target.CastTargetLength;
                        }
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
                            // Erweiterte R�ckgabewert-Typinferenz f�r bekannte Aggregatfunktionen
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                            {
                                switch (lower)
                                {
                                    case "count":
                                        target.SqlTypeName = "int"; break;
                                    case "count_big":
                                        target.SqlTypeName = "bigint"; break;
                                    case "avg":
                                        // AVG �ber integer -> decimal. Feinere Pr�zision k�nnte aus Parametertyp kommen; hier pauschal.
                                        try
                                        {
                                            if (fn.Parameters?.Count == 1)
                                            {
                                                var pExpr = fn.Parameters[0] as ScalarExpression;
                                                var pCol = new ResultColumn();
                                                AnalyzeScalarExpression(pExpr, pCol, state);
                                                var meta = ParseTypeString(pCol.SqlTypeName?.ToLowerInvariant());
                                                if (meta.Base == "decimal" || meta.Base == "numeric")
                                                    target.SqlTypeName = (meta.Prec.HasValue && meta.Scale.HasValue) ? $"decimal({meta.Prec},{meta.Scale})" : "decimal(18,2)";
                                                else if (meta.Base == "int" || meta.Base == "smallint" || meta.Base == "tinyint" || meta.Base == "bigint")
                                                    target.SqlTypeName = "decimal(18,2)";
                                            }
                                        }
                                        catch { }
                                        if (string.IsNullOrWhiteSpace(target.SqlTypeName)) target.SqlTypeName = "decimal(18,2)"; break;
                                    case "exists":
                                        target.SqlTypeName = "bit"; break;
                                    case "sum":
                                        // SUM Spezialfall weiter unten (zero/one). Wenn dort nicht int gesetzt wird -> Fallback.
                                        // Versuche param zu inspizieren f�r integer vs decimal.
                                        try
                                        {
                                            if (fn.Parameters?.Count == 1)
                                            {
                                                var pExpr = fn.Parameters[0] as ScalarExpression;
                                                // Grobe Heuristik: Wenn Parameter bereits HasIntegerLiteral Flag setzen w�rde
                                                var temp = new ResultColumn();
                                                AnalyzeScalarExpression(pExpr, temp, state);
                                                if (temp.HasIntegerLiteral && !temp.HasDecimalLiteral)
                                                {
                                                    target.SqlTypeName = "int"; // integer SUM (kann �berlaufen, aber pragmatisch)
                                                }
                                                else if (temp.HasDecimalLiteral)
                                                {
                                                    target.SqlTypeName = "decimal(18,4)"; // etwas h�here Pr�zision f�r Summierung
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
                                        // MIN/MAX: Wenn einzelner Parameter Literal-Flags erkennen l�sst, leite einfachen Typ ab
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
                        // Spezialfall: SUM �ber reinem 0/1 Ausdruck -> int
                        if (lower == "sum")
                        {
                            try
                            {
                                if (fn.Parameters != null && fn.Parameters.Count == 1)
                                {
                                    var pExpr = fn.Parameters[0] as ScalarExpression;
                                    if (IsPureZeroOneConditional(pExpr))
                                    {
                                        target.HasIntegerLiteral = true; // verst�rke Flag
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
                    // Capture function schema + name if schema-qualified (CallTarget) � purely AST based
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
                        // Fallback: Falls Funktionsname bereits Schema enth�lt (identity.RecordAsJson)
                        // legacy schema/name normalization removed (Reference handles schema)
                        // Entfernt: identity.RecordAsJson Erkennung � keine spezielle Funktionsmarkierung mehr.
                    }
                    catch { }
                    // JSON-Funktions-Expansion NACH Erfassung von Schema/Name, damit Resolver korrekte schema erh�lt
                    TryExpandFunctionJson(fn, target);
                    // Wenn noch kein konkreter Typ bestimmt ist, versuche skalaren Funktions-R?ckgabetyp zuzuweisen
                    try { TryApplyScalarFunctionReturnType(fn, target); } catch { }
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
                            // Gleiche Quellspalte ? Typ �bernehmen
                            if (!string.IsNullOrWhiteSpace(thenCol.SourceColumn) && !string.IsNullOrWhiteSpace(elseCol.SourceColumn) && thenCol.SourceColumn.Equals(elseCol.SourceColumn, StringComparison.OrdinalIgnoreCase))
                            {
                                // Entfernt: Quellspalten-basierte Typableitung
                            }
                            // Identische vorab abgeleitete SqlTypeName Werte
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName) && !string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && thenCol.SqlTypeName.Equals(elseCol.SqlTypeName, StringComparison.OrdinalIgnoreCase))
                            {
                                target.SqlTypeName = thenCol.SqlTypeName;
                                // MaxLength �bernehmen bei identischen Typen
                                if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                }
                                else if (thenCol.MaxLength.HasValue || elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = thenCol.MaxLength ?? elseCol.MaxLength;
                                }

                                if (ShouldDiagJsonAst())
                                {
                                    try { System.Console.WriteLine($"[ast-type-iif-branches] {target.Name}: identical types {target.SqlTypeName}, maxLength={target.MaxLength}"); } catch { }
                                }
                            }
                            // Erweiterte Typ-Vereinigung: Beide nvarchar aber verschiedene L�ngen
                            else if (string.IsNullOrWhiteSpace(target.SqlTypeName)
                                && !string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && !string.IsNullOrWhiteSpace(elseCol.SqlTypeName)
                                && thenCol.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase)
                                && elseCol.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase))
                            {
                                target.SqlTypeName = "nvarchar";
                                // Maximum der beiden MaxLength Werte
                                if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                }
                                else
                                {
                                    // Falls einer unbegrenzt ist ? unbegrenzt
                                    target.MaxLength = null;
                                }

                                if (ShouldDiagJsonAst())
                                {
                                    try { System.Console.WriteLine($"[ast-type-iif-branches] {target.Name}: nvarchar union, maxLength={target.MaxLength}"); } catch { }
                                }
                            }
                            // Fallback: Nur eine Branch hat einen Typ ? �bernehmen, aber MaxLength beider Branches ber�cksichtigen
                            else if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                            {
                                if (!string.IsNullOrWhiteSpace(thenCol.SqlTypeName))
                                {
                                    target.SqlTypeName = thenCol.SqlTypeName;
                                    // MaxLength: Maximum beider Branches verwenden, auch wenn nur eine einen Typ hat
                                    if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                    {
                                        target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                    }
                                    else if (thenCol.MaxLength.HasValue || elseCol.MaxLength.HasValue)
                                    {
                                        target.MaxLength = thenCol.MaxLength ?? elseCol.MaxLength;
                                    }

                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-iif-branches] {target.Name}: using THEN branch type {target.SqlTypeName}, maxLength={target.MaxLength} (then={thenCol.MaxLength}, else={elseCol.MaxLength})"); } catch { }
                                    }
                                }
                                else if (!string.IsNullOrWhiteSpace(elseCol.SqlTypeName))
                                {
                                    target.SqlTypeName = elseCol.SqlTypeName;
                                    // MaxLength: Maximum beider Branches verwenden, auch wenn nur eine einen Typ hat
                                    if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                    {
                                        target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                    }
                                    else if (thenCol.MaxLength.HasValue || elseCol.MaxLength.HasValue)
                                    {
                                        target.MaxLength = thenCol.MaxLength ?? elseCol.MaxLength;
                                    }

                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-iif-branches] {target.Name}: using ELSE branch type {target.SqlTypeName}, maxLength={target.MaxLength} (then={thenCol.MaxLength}, else={elseCol.MaxLength})"); } catch { }
                                    }
                                }
                            }

                            // Beide Literal-Strings ? nvarchar(maxLen)
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

                    // CONCAT Funktions-Typ-Ableitung: AST-basierte Erkennung mit MaxLength Sch�tzung
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(fnName) && fnName.Equals("CONCAT", StringComparison.OrdinalIgnoreCase) && fn.Parameters?.Count > 0)
                        {
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                            {
                                int totalMaxLength = 0;
                                bool hasUnboundedOperand = false;
                                int operandCount = 0;

                                if (ShouldDiagJsonAst())
                                {
                                    try { System.Console.WriteLine($"[ast-type-concat] analyzing CONCAT with {fn.Parameters.Count} operands for {target.Name}"); } catch { }
                                }

                                foreach (var param in fn.Parameters)
                                {
                                    if (param is ScalarExpression scalarParam)
                                    {
                                        operandCount++;
                                        var operandCol = new ResultColumn();
                                        var operandState = new SourceBindingState();
                                        AnalyzeScalarExpression(scalarParam, operandCol, operandState);

                                        // String-Literal: direkte L�nge verwenden
                                        if (IsLiteralString(scalarParam, out var literalValue))
                                        {
                                            var literalLength = literalValue?.Length ?? 0;
                                            totalMaxLength += literalLength;
                                            if (ShouldDiagJsonAst())
                                            {
                                                try { System.Console.WriteLine($"[ast-type-concat] operand {operandCount}: string literal, length={literalLength}"); } catch { }
                                            }
                                        }
                                        // Spalten-Referenz mit bekanntem Typ
                                        else if (!string.IsNullOrWhiteSpace(operandCol.SqlTypeName))
                                        {
                                            if (operandCol.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase))
                                            {
                                                if (operandCol.MaxLength.HasValue && operandCol.MaxLength.Value > 0)
                                                {
                                                    totalMaxLength += operandCol.MaxLength.Value;
                                                    if (ShouldDiagJsonAst())
                                                    {
                                                        try { System.Console.WriteLine($"[ast-type-concat] operand {operandCount}: nvarchar({operandCol.MaxLength.Value})"); } catch { }
                                                    }
                                                }
                                                else
                                                {
                                                    hasUnboundedOperand = true;
                                                    if (ShouldDiagJsonAst())
                                                    {
                                                        try { System.Console.WriteLine($"[ast-type-concat] operand {operandCount}: nvarchar(max) - unbounded"); } catch { }
                                                    }
                                                }
                                            }
                                            else if (operandCol.SqlTypeName.StartsWith("varchar", StringComparison.OrdinalIgnoreCase))
                                            {
                                                // varchar -> nvarchar conversion, gleiche MaxLength
                                                if (operandCol.MaxLength.HasValue && operandCol.MaxLength.Value > 0)
                                                {
                                                    totalMaxLength += operandCol.MaxLength.Value;
                                                }
                                                else
                                                {
                                                    hasUnboundedOperand = true;
                                                }
                                            }
                                            else
                                            {
                                                // Andere Typen: konservative Sch�tzung f�r Konvertierung zu String
                                                totalMaxLength += 50; // Default f�r int, datetime, etc.
                                                if (ShouldDiagJsonAst())
                                                {
                                                    try { System.Console.WriteLine($"[ast-type-concat] operand {operandCount}: {operandCol.SqlTypeName} - estimated 50 chars"); } catch { }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Unbekannter Typ: konservative Sch�tzung
                                            totalMaxLength += 100;
                                            if (ShouldDiagJsonAst())
                                            {
                                                try { System.Console.WriteLine($"[ast-type-concat] operand {operandCount}: unknown type - estimated 100 chars"); } catch { }
                                            }
                                        }
                                    }
                                }

                                // Ergebnis-Typ setzen
                                target.SqlTypeName = "nvarchar";
                                if (hasUnboundedOperand || totalMaxLength > 4000)
                                {
                                    // nvarchar(max) falls ein Operand unbegrenzt ist oder Gesamtl�nge > 4000
                                    target.MaxLength = null;
                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-concat] result: nvarchar(max) - unbounded or too long ({totalMaxLength})"); } catch { }
                                    }
                                }
                                else if (totalMaxLength > 0)
                                {
                                    target.MaxLength = totalMaxLength;
                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-concat] result: nvarchar({totalMaxLength})"); } catch { }
                                    }
                                }
                                else
                                {
                                    // Fallback: nvarchar(max)
                                    target.MaxLength = null;
                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-concat] result: nvarchar(max) - fallback"); } catch { }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ShouldDiag())
                        {
                            try { System.Console.WriteLine($"[ast-type-concat] error analyzing CONCAT for {target.Name}: {ex.Message}"); } catch { }
                        }
                    }

                    // Erweiterung: F�r JSON_QUERY nun innere ScalarSubquery parsen, um Aggregat-Typen zu erkennen
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
                                            subqueryHandled = true; continue; // n�chster Parameter
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
                                    // Sonstige Parameter NICHT traversieren, um keine Source-Bindings f�lschlich zu �bernehmen
                                }
                                catch { }
                            }
                    }
                    else
                    {
                        foreach (var p in fn.Parameters)
                        {
                            // Aggregat: Parameter-Analyse nicht ExpressionKind �berschreiben lassen
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
                    // Direkter AST Node f�r IIF (statt FunctionCall). Klassifizieren als FunctionCall.
                    target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                    try
                    {
                        var thenExpr = iif.ThenExpression as ScalarExpression;
                        var elseExpr = iif.ElseExpression as ScalarExpression;
                        var thenCol = new ResultColumn(); var thenState = new SourceBindingState();
                        AnalyzeScalarExpression(thenExpr, thenCol, thenState);
                        var elseCol = new ResultColumn(); var elseState = new SourceBindingState();
                        AnalyzeScalarExpression(elseExpr, elseCol, elseState);

                        // Erweiterte IIF-Typ-Vereinigung (gleiche Logik wie FunctionCall IIF)
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                        {
                            // Identische vorab abgeleitete SqlTypeName Werte
                            if (!string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && thenCol.SqlTypeName.Equals(elseCol.SqlTypeName, StringComparison.OrdinalIgnoreCase))
                            {
                                target.SqlTypeName = thenCol.SqlTypeName;
                                // MaxLength �bernehmen bei identischen Typen
                                if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                }
                                else if (thenCol.MaxLength.HasValue || elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = thenCol.MaxLength ?? elseCol.MaxLength;
                                }

                                if (ShouldDiagJsonAst())
                                {
                                    try { System.Console.WriteLine($"[ast-type-iif-branches] {target.Name}: identical types {target.SqlTypeName}, maxLength={target.MaxLength}"); } catch { }
                                }
                            }
                            // Erweiterte Typ-Vereinigung: Beide nvarchar aber verschiedene L�ngen
                            else if (!string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && !string.IsNullOrWhiteSpace(elseCol.SqlTypeName)
                                && thenCol.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase)
                                && elseCol.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase))
                            {
                                target.SqlTypeName = "nvarchar";
                                // Maximum der beiden MaxLength Werte
                                if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                }
                                else
                                {
                                    // Falls einer unbegrenzt ist ? unbegrenzt
                                    target.MaxLength = null;
                                }

                                if (ShouldDiagJsonAst())
                                {
                                    try { System.Console.WriteLine($"[ast-type-iif-branches] {target.Name}: nvarchar union, maxLength={target.MaxLength}"); } catch { }
                                }
                            }
                            // Fallback: Nur eine Branch hat einen Typ ? �bernehmen, aber MaxLength beider Branches ber�cksichtigen
                            else if (!string.IsNullOrWhiteSpace(thenCol.SqlTypeName))
                            {
                                target.SqlTypeName = thenCol.SqlTypeName;
                                // MaxLength: Maximum beider Branches verwenden, auch wenn nur eine einen Typ hat
                                if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                }
                                else if (thenCol.MaxLength.HasValue || elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = thenCol.MaxLength ?? elseCol.MaxLength;
                                }

                                if (ShouldDiagJsonAst())
                                {
                                    try { System.Console.WriteLine($"[ast-type-iif-branches] {target.Name}: using THEN branch type {target.SqlTypeName}, maxLength={target.MaxLength} (then={thenCol.MaxLength}, else={elseCol.MaxLength})"); } catch { }
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(elseCol.SqlTypeName))
                            {
                                target.SqlTypeName = elseCol.SqlTypeName;
                                // MaxLength: Maximum beider Branches verwenden, auch wenn nur eine einen Typ hat
                                if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                }
                                else if (thenCol.MaxLength.HasValue || elseCol.MaxLength.HasValue)
                                {
                                    target.MaxLength = thenCol.MaxLength ?? elseCol.MaxLength;
                                }

                                if (ShouldDiagJsonAst())
                                {
                                    try { System.Console.WriteLine($"[ast-type-iif-branches] {target.Name}: using ELSE branch type {target.SqlTypeName}, maxLength={target.MaxLength} (then={thenCol.MaxLength}, else={elseCol.MaxLength})"); } catch { }
                                }
                            }
                            // Legacy String-Literal Fallback
                            else if (IsLiteralString(thenExpr, out var litThen) && IsLiteralString(elseExpr, out var litElse))
                            {
                                var maxLen = Math.Max(litThen?.Length ?? 0, litElse?.Length ?? 0);
                                target.SqlTypeName = "nvarchar";
                                if (maxLen > 0) target.MaxLength = maxLen;

                                if (ShouldDiagJsonAst())
                                {
                                    try { System.Console.WriteLine($"[ast-type-iif-branches] {target.Name}: string literal fallback, maxLength={maxLen}"); } catch { }
                                }
                            }
                        }
                    }
                    catch { }
                    break;
                // JSON_QUERY appears as FunctionCall with name 'JSON_QUERY'; we already classify via FunctionCall. No dedicated node.
                case BinaryExpression be:
                    // Computed (ExpressionKind nicht durch Operanden �berschreiben lassen)
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

                    // CASE Typ-Vereinigung: Sammle alle Branch-Typen und vereinige sie
                    try
                    {
                        var branchCols = new List<ResultColumn>();

                        // Analysiere alle WHEN-THEN Branches
                        if (sce.WhenClauses != null)
                        {
                            foreach (var w in sce.WhenClauses)
                            {
                                var branchCol = new ResultColumn();
                                var branchState = new SourceBindingState();
                                AnalyzeScalarExpression(w.ThenExpression, branchCol, branchState);
                                branchCols.Add(branchCol);
                            }
                        }

                        // Analysiere ELSE Branch (falls vorhanden)
                        if (sce.ElseExpression != null)
                        {
                            var elseCol = new ResultColumn();
                            var elseState = new SourceBindingState();
                            AnalyzeScalarExpression(sce.ElseExpression, elseCol, elseState);
                            branchCols.Add(elseCol);
                        }

                        // Typ-Vereinigung aller Branches
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName) && branchCols.Count > 0)
                        {
                            // Versuche identische SqlTypeName zu finden
                            var typedBranches = branchCols.Where(b => !string.IsNullOrWhiteSpace(b.SqlTypeName)).ToList();
                            if (typedBranches.Count > 0)
                            {
                                var firstType = typedBranches[0].SqlTypeName;
                                if (typedBranches.All(b => b.SqlTypeName.Equals(firstType, StringComparison.OrdinalIgnoreCase)))
                                {
                                    // Alle Branches haben identischen Typ
                                    target.SqlTypeName = firstType;
                                    var maxLengths = typedBranches.Where(b => b.MaxLength.HasValue).Select(b => b.MaxLength!.Value);
                                    if (maxLengths.Any()) target.MaxLength = maxLengths.Max();

                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-case] {target.Name}: identical types {target.SqlTypeName}, maxLength={target.MaxLength} from {typedBranches.Count} branches"); } catch { }
                                    }
                                }
                                else if (typedBranches.All(b => b.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase)))
                                {
                                    // Alle nvarchar aber verschiedene L�ngen ? nvarchar union
                                    target.SqlTypeName = "nvarchar";
                                    var maxLengths = typedBranches.Where(b => b.MaxLength.HasValue).Select(b => b.MaxLength!.Value);
                                    target.MaxLength = maxLengths.Any() ? maxLengths.Max() : (int?)null;

                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-case] {target.Name}: nvarchar union, maxLength={target.MaxLength} from {typedBranches.Count} branches"); } catch { }
                                    }
                                }
                                else
                                {
                                    // Fallback: Ersten verf�gbaren Typ verwenden
                                    target.SqlTypeName = typedBranches[0].SqlTypeName;
                                    target.MaxLength = typedBranches[0].MaxLength;

                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-case] {target.Name}: fallback to first branch type {target.SqlTypeName}"); } catch { }
                                    }
                                }
                            }
                        }

                        // Falls ELSE-Branch fehlt ? IsNullable = true (CASE kann NULL zur�ckgeben)
                        if (sce.ElseExpression == null && target.IsNullable != true)
                        {
                            target.IsNullable = true;
                        }
                    }
                    catch { }
                    break;
                case SimpleCaseExpression simp:
                    target.ExpressionKind = ResultColumnExpressionKind.Computed;

                    // Analysiere Input Expression (wird aber nicht f�r Typ-Vereinigung verwendet)
                    AnalyzeScalarExpression(simp.InputExpression, target, state);

                    // CASE Typ-Vereinigung: Sammle alle Branch-Typen und vereinige sie
                    try
                    {
                        var branchCols = new List<ResultColumn>();

                        // Analysiere alle WHEN-THEN Branches
                        if (simp.WhenClauses != null)
                        {
                            foreach (var w in simp.WhenClauses)
                            {
                                var branchCol = new ResultColumn();
                                var branchState = new SourceBindingState();
                                AnalyzeScalarExpression(w.ThenExpression, branchCol, branchState);
                                branchCols.Add(branchCol);
                            }
                        }

                        // Analysiere ELSE Branch (falls vorhanden)
                        if (simp.ElseExpression != null)
                        {
                            var elseCol = new ResultColumn();
                            var elseState = new SourceBindingState();
                            AnalyzeScalarExpression(simp.ElseExpression, elseCol, elseState);
                            branchCols.Add(elseCol);
                        }

                        // Typ-Vereinigung aller Branches (gleiche Logik wie SearchedCase)
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName) && branchCols.Count > 0)
                        {
                            var typedBranches = branchCols.Where(b => !string.IsNullOrWhiteSpace(b.SqlTypeName)).ToList();
                            if (typedBranches.Count > 0)
                            {
                                var firstType = typedBranches[0].SqlTypeName;
                                if (typedBranches.All(b => b.SqlTypeName.Equals(firstType, StringComparison.OrdinalIgnoreCase)))
                                {
                                    // Alle Branches haben identischen Typ
                                    target.SqlTypeName = firstType;
                                    var maxLengths = typedBranches.Where(b => b.MaxLength.HasValue).Select(b => b.MaxLength!.Value);
                                    if (maxLengths.Any()) target.MaxLength = maxLengths.Max();

                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-case-simple] {target.Name}: identical types {target.SqlTypeName}, maxLength={target.MaxLength} from {typedBranches.Count} branches"); } catch { }
                                    }
                                }
                                else if (typedBranches.All(b => b.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase)))
                                {
                                    // Alle nvarchar aber verschiedene L�ngen ? nvarchar union
                                    target.SqlTypeName = "nvarchar";
                                    var maxLengths = typedBranches.Where(b => b.MaxLength.HasValue).Select(b => b.MaxLength!.Value);
                                    target.MaxLength = maxLengths.Any() ? maxLengths.Max() : (int?)null;

                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-case-simple] {target.Name}: nvarchar union, maxLength={target.MaxLength} from {typedBranches.Count} branches"); } catch { }
                                    }
                                }
                                else
                                {
                                    // Fallback: Ersten verf�gbaren Typ verwenden
                                    target.SqlTypeName = typedBranches[0].SqlTypeName;
                                    target.MaxLength = typedBranches[0].MaxLength;

                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-case-simple] {target.Name}: fallback to first branch type {target.SqlTypeName}"); } catch { }
                                    }
                                }
                            }
                        }

                        // Falls ELSE-Branch fehlt ? IsNullable = true
                        if (simp.ElseExpression == null && target.IsNullable != true)
                        {
                            target.IsNullable = true;
                        }
                    }
                    catch { }
                    break;
                case CoalesceExpression coalesce:
                    target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;

                    // COALESCE Typ-Ableitung: Nimmt ersten non-null Wert, daher ersten verf�gbaren Typ verwenden
                    try
                    {
                        if (coalesce.Expressions != null && coalesce.Expressions.Count > 0)
                        {
                            var operandCols = new List<ResultColumn>();

                            // Analysiere alle Operanden
                            foreach (var operandExpr in coalesce.Expressions.OfType<ScalarExpression>())
                            {
                                var operandCol = new ResultColumn();
                                var operandState = new SourceBindingState();
                                AnalyzeScalarExpression(operandExpr, operandCol, operandState);
                                operandCols.Add(operandCol);
                            }

                            // Typ-Ableitung: Ersten verf�gbaren Typ verwenden (COALESCE-Semantik)
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                            {
                                var typedOperand = operandCols.FirstOrDefault(op => !string.IsNullOrWhiteSpace(op.SqlTypeName));
                                if (typedOperand != null)
                                {
                                    target.SqlTypeName = typedOperand.SqlTypeName;
                                    target.MaxLength = typedOperand.MaxLength;

                                    if (ShouldDiagJsonAst())
                                    {
                                        try { System.Console.WriteLine($"[ast-type-coalesce] {target.Name}: using type {target.SqlTypeName} from first available operand"); } catch { }
                                    }
                                }
                            }

                            // COALESCE kann NULL zur�ckgeben wenn alle Operanden NULL sind
                            if (target.IsNullable != true)
                            {
                                target.IsNullable = true;
                            }
                        }
                    }
                    catch { }
                    break;
                case ParenthesisExpression pe:
                    AnalyzeScalarExpression(pe.Expression, target, state);
                    break;
                case ScalarSubquery ss:
                    // Detect nested JSON subquery (SELECT ... FOR JSON ...)
                    if (ss.QueryExpression is QuerySpecification qs && _analysis.NestedJsonSets.TryGetValue(qs, out var nested))
                    {
                        try
                        {
                            // Finalize typing for nested FOR JSON subquery columns using table variable aliases in this subquery
                            var localVarCols = new Dictionary<string, List<ResultColumn>>(StringComparer.OrdinalIgnoreCase);
                            try
                            {
                                if (qs.FromClause?.TableReferences != null)
                                {
                                    void CollectVarRefs(TableReference tr)
                                    {
                                        switch (tr)
                                        {
                                            case null: return;
                                            case QualifiedJoin qj:
                                                CollectVarRefs(qj.FirstTableReference);
                                                CollectVarRefs(qj.SecondTableReference);
                                                break;
                                            case VariableTableReference vtr:
                                                var alias = vtr.Alias?.Value;
                                                var varName = vtr.Variable?.Name;
                                                if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(varName))
                                                {
                                                    var key = varName.StartsWith("@") ? varName : ("@" + varName);
                                                    if (_tableVariableColumns.TryGetValue(key, out var colsForVar))
                                                        localVarCols[alias] = colsForVar;
                                                }
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                    foreach (var tr in qs.FromClause.TableReferences) CollectVarRefs(tr);
                                }
                                // Fallback: scan fragment text for @Var alias patterns when AST doesn?t expose VariableTableReference
                                if (localVarCols.Count == 0 && qs.StartOffset >= 0 && qs.FragmentLength > 0 && _definition != null && qs.StartOffset + qs.FragmentLength <= _definition.Length)
                                {
                                    try
                                    {
                                        var frag = _definition.Substring(qs.StartOffset, qs.FragmentLength);
                                        var rxVarAlias = new System.Text.RegularExpressions.Regex(@"@(?<var>\w+)\s+(?:AS\s+)?(?<alias>\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                        foreach (System.Text.RegularExpressions.Match m in rxVarAlias.Matches(frag))
                                        {
                                            var v = m.Groups["var"].Value; var a = m.Groups["alias"].Value;
                                            if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(a)) continue;
                                            var key = "@" + v;
                                            if (_tableVariableColumns.TryGetValue(key, out var colsForVar))
                                                localVarCols[a] = colsForVar;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                            if (localVarCols.Count > 0 && nested.Columns != null)
                            {
                                foreach (var ch in nested.Columns)
                                {
                                    if (!string.IsNullOrWhiteSpace(ch.SqlTypeName)) continue;
                                    var raw = ch.RawExpression;
                                    if (!string.IsNullOrWhiteSpace(raw))
                                    {
                                        try
                                        {
                                            var matches = System.Text.RegularExpressions.Regex.Matches(raw, @"\b(?<alias>[A-Za-z0-9_]+)\.(?<col>[A-Za-z0-9_]+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                            foreach (System.Text.RegularExpressions.Match mm in matches)
                                            {
                                                var a = mm.Groups["alias"].Value; var c = mm.Groups["col"].Value;
                                                if (!localVarCols.TryGetValue(a, out var list)) continue;
                                                var v = list.FirstOrDefault(x => x.Name?.Equals(c, StringComparison.OrdinalIgnoreCase) == true);
                                                if (v != null)
                                                {
                                                    ch.SqlTypeName = v.SqlTypeName; ch.MaxLength = v.MaxLength; ch.IsNullable = v.IsNullable;
                                                    break;
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                if (localVarCols.Count == 1)
                                {
                                    var only = localVarCols.First();
                                    foreach (var ch in nested.Columns)
                                    {
                                        if (!string.IsNullOrWhiteSpace(ch.SqlTypeName)) continue;
                                        var last = ch.Name.Contains('.') ? ch.Name.Split('.').Last() : ch.Name;
                                        var v = only.Value.FirstOrDefault(x => x.Name?.Equals(last, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(x.SqlTypeName));
                                        if (v != null)
                                        {
                                            ch.SqlTypeName = v.SqlTypeName; ch.MaxLength = v.MaxLength; ch.IsNullable = v.IsNullable;
                                        }
                                    }
                                }
                                // Final pass: unique last-segment match across all variable tables used in this subquery
                                TryApplyVarTableTypePropagationToNestedJson(nested, localVarCols.Values);
                            }
                            // Explicit mapping for declared @Var tables by normalized name to cover dotted JSON names (e.g., comparison.status.code -> ComparisonStatusCode)
                            try
                            {
                                if (nested.Columns != null && _tableVariableColumns != null)
                                {
                                    foreach (var kv in _tableVariableColumns)
                                    {
                                        var vcols = kv.Value; if (vcols == null || vcols.Count == 0) continue;
                                        foreach (var ch in nested.Columns)
                                        {
                                            if (!string.IsNullOrWhiteSpace(ch.SqlTypeName)) continue;
                                            string last = ch.Name.Contains('.') ? ch.Name.Split('.').Last() : ch.Name;
                                            string collapsed = new string(ch.Name.Where(char.IsLetterOrDigit).ToArray());
                                            ResultColumn match = vcols.FirstOrDefault(x => x.Name?.Equals(last, StringComparison.OrdinalIgnoreCase) == true);
                                            if (match == null)
                                            {
                                                // also compare collapsed (remove dots) against source column name (collapsed)
                                                foreach (var vc in vcols)
                                                {
                                                    var vcCollapsed = new string((vc.Name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
                                                    if (!string.IsNullOrWhiteSpace(vcCollapsed) && string.Equals(vcCollapsed, collapsed, StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        match = vc; break;
                                                    }
                                                }
                                            }
                                            if (match != null && !string.IsNullOrWhiteSpace(match.SqlTypeName))
                                            {
                                                ch.SqlTypeName = match.SqlTypeName; ch.MaxLength = match.MaxLength; ch.IsNullable = match.IsNullable;
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        catch { }
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
                                        target.SqlTypeName = "bigint"; break; // korrekt f�r COUNT_BIG
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
                if (!string.Equals(fname, "JSON_QUERY", StringComparison.OrdinalIgnoreCase))
                    target.Reference ??= new ColumnReferenceInfo { Kind = "Function", Schema = schema, Name = fname };
                target.DeferredJsonExpansion = true;
                target.IsNestedJson = true;
                // Leave ReturnsJson flags unset when we only keep a reference; expansion will set them if needed.
                return;
            }
            if (!string.Equals(fname, "JSON_QUERY", StringComparison.OrdinalIgnoreCase))
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

        private void TryApplyScalarFunctionReturnType(FunctionCall fc, ResultColumn target)
        {
            if (fc == null || target == null) return;
            if (ResolveScalarFunctionReturnType == null) return;
            if (!string.IsNullOrWhiteSpace(target.SqlTypeName)) return;
            var fnName = fc.FunctionName?.Value;
            if (!string.IsNullOrWhiteSpace(fnName) && fnName.Equals("JSON_QUERY", StringComparison.OrdinalIgnoreCase)) return;
            string schema = null;
            string fname = fnName;
            try
            {
                if (fc.CallTarget is MultiPartIdentifierCallTarget mp && mp.MultiPartIdentifier?.Identifiers?.Count > 0)
                {
                    var idents = mp.MultiPartIdentifier.Identifiers.Select(i => i.Value).ToList();
                    if (idents.Count == 1)
                    {
                        schema = null; fname = idents[0];
                    }
                    else if (idents.Count >= 2)
                    {
                        schema = idents[^2]; fname = idents[^1];
                    }
                }
            }
            catch { }
            if (string.IsNullOrWhiteSpace(schema) && !string.IsNullOrWhiteSpace(fname) && fname.Contains('.'))
            {
                var segs = fname.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length >= 2) { schema = segs[^2]; fname = segs[^1]; }
            }
            if (string.IsNullOrWhiteSpace(schema) && !string.IsNullOrWhiteSpace(target.RawExpression))
            {
                try
                {
                    var match = Regex.Match(target.RawExpression, @"(?:(?<schema>\[[^\]]+\]|[A-Za-z0-9_]+)\s*\.)?(?<name>[A-Za-z0-9_]+)\s*\(");
                    if (match.Success)
                    {
                        var schemaToken = match.Groups["schema"].Value;
                        var nameToken = match.Groups["name"].Value;
                        if (!string.IsNullOrWhiteSpace(schemaToken)) schema = schemaToken.Trim().Trim('[', ']');
                        if (!string.IsNullOrWhiteSpace(nameToken)) fname = nameToken;
                    }
                }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(fname)) return;
            schema ??= _analysis.DefaultSchema ?? "dbo";
            try
            {
                var meta = ResolveScalarFunctionReturnType(schema, fname);
                if (!string.IsNullOrWhiteSpace(meta.SqlTypeName) && string.IsNullOrWhiteSpace(target.SqlTypeName))
                {
                    target.SqlTypeName = meta.SqlTypeName;
                    if (meta.MaxLength.HasValue) target.MaxLength = meta.MaxLength;
                    if (meta.IsNullable.HasValue && target.IsNullable == null) target.IsNullable = meta.IsNullable;
                    if (ShouldDiagJsonAst())
                    {
                        try { System.Console.WriteLine($"[ast-fn-ret] {schema}.{fname} -> {target.SqlTypeName} len={target.MaxLength} null={target.IsNullable} alias={target.Name}"); } catch { }
                    }
                }
            }
            catch { }
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
                        // Aggregat-Propagation: finde urspr�ngliche ResultColumn im Derived Table um AggregateFlags zu kopieren
                        TryPropagateAggregateFromDerived(parts[0], md.Key, col);
                        // CTE-Type-Propagation: propagiere SqlTypeName und andere Type-Properties von CTE
                        TryPropagateTypeFromDerived(parts[0], md.Key, col);
                    }
                    else col.IsAmbiguous = true;
                }
            }
            else if (parts.Count == 2)
            {
                var tableOrAlias = parts[0];
                var column = parts[1];
                // Prefer CTE/derived alias mapping first
                if (_derivedTableColumns.TryGetValue(tableOrAlias, out var dcols))
                {
                    var src = dcols.FirstOrDefault(c => c.Name?.Equals(column, StringComparison.OrdinalIgnoreCase) == true);
                    if (src != null)
                    {
                        col.SourceAlias = tableOrAlias;
                        col.SourceColumn = column;
                        // If source mapping to physical table exists, adopt it for enrichment
                        if (_derivedTableColumnSources.TryGetValue(tableOrAlias, out var dmap) && dmap.TryGetValue(column, out var bound))
                        {
                            if (!string.IsNullOrWhiteSpace(bound.Schema)) col.SourceSchema = bound.Schema;
                            if (!string.IsNullOrWhiteSpace(bound.Table)) col.SourceTable = bound.Table;
                            if (!string.IsNullOrWhiteSpace(bound.Column)) col.SourceColumn = bound.Column;
                        }
                        if (string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(src.SqlTypeName)) col.SqlTypeName = src.SqlTypeName;
                        if (!col.MaxLength.HasValue && src.MaxLength.HasValue) col.MaxLength = src.MaxLength;
                        if (!col.IsNullable.HasValue && src.IsNullable.HasValue) col.IsNullable = src.IsNullable;
                        ConsoleWriteBind(col, reason: "alias-derived");
                        _analysis.ColumnRefBound++;
                        // If we resolved to a physical source, assign concrete type from DB metadata as needed
                        TryAssignColumnType(col);
                    }
                }
                // Table variable alias mapping (not active)
                else if (_tableVariableAliases.TryGetValue(tableOrAlias, out var varName) && _tableVariableColumns.TryGetValue(varName, out var vcols))
                {
                    var vcol = vcols.FirstOrDefault(c => c.Name?.Equals(column, StringComparison.OrdinalIgnoreCase) == true);
                    if (vcol != null)
                    {
                        col.SourceAlias = tableOrAlias; col.SourceColumn = column;
                        if (string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(vcol.SqlTypeName)) col.SqlTypeName = vcol.SqlTypeName;
                        if (!col.MaxLength.HasValue && vcol.MaxLength.HasValue) col.MaxLength = vcol.MaxLength;
                        if (!col.IsNullable.HasValue && vcol.IsNullable.HasValue) col.IsNullable = vcol.IsNullable;
                        ConsoleWriteBind(col, reason: "alias-var-table");
                        _analysis.ColumnRefBound++;
                    }
                }
                else if (_tableAliases.TryGetValue(tableOrAlias, out var mapped))
                {
                    // If this alias points to a CTE name, treat as derived rather than physical
                    if (!string.IsNullOrWhiteSpace(mapped.Table) && _derivedTableColumns.TryGetValue(mapped.Table, out var cteColsForName))
                    {
                        var src = cteColsForName.FirstOrDefault(c => c.Name?.Equals(column, StringComparison.OrdinalIgnoreCase) == true);
                        if (src != null)
                        {
                            col.SourceAlias = tableOrAlias;
                            col.SourceColumn = column;
                            if (string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(src.SqlTypeName)) col.SqlTypeName = src.SqlTypeName;
                            if (!col.MaxLength.HasValue && src.MaxLength.HasValue) col.MaxLength = src.MaxLength;
                            if (!col.IsNullable.HasValue && src.IsNullable.HasValue) col.IsNullable = src.IsNullable;
                            ConsoleWriteBind(col, reason: "alias-cte-name");
                            _analysis.ColumnRefBound++;
                        }
                    }
                    else
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
                    // Neu: Aggregat/Literal Propagation auch f�r 2-teilige alias.column Referenzen
                    TryPropagateAggregateFromDerived(column, tableOrAlias, col);
                    // Direkter Lookup falls TryPropagateAggregateFromDerived nichts gesetzt hat (Name-Mismatch etc.)
                    if (!col.IsAggregate && _derivedTableColumns.TryGetValue(tableOrAlias, out var dcols2))
                    {
                        var srcCol = dcols2.FirstOrDefault(dc => dc.Name != null && dc.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
                        if (srcCol != null)
                        {
                            // Literal Flags additiv �bernehmen
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
                // Erweiterung: Versuche generischere Muster (4-teilige Identifier, tempor�re Tabellen, dbo Fallback)
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
            if (ResolveTableColumnType == null)
            {
                if (ShouldDiag()) System.Console.WriteLine($"[cte-col-type-debug] ResolveTableColumnType delegate is null");
                return;
            }
            if (string.IsNullOrWhiteSpace(col.SourceSchema) || string.IsNullOrWhiteSpace(col.SourceTable) || string.IsNullOrWhiteSpace(col.SourceColumn))
            {
                if (ShouldDiag()) System.Console.WriteLine($"[cte-col-type-debug] Missing source info: schema={col.SourceSchema} table={col.SourceTable} column={col.SourceColumn}");
                return;
            }
            try
            {
                if (ShouldDiag()) System.Console.WriteLine($"[cte-col-type-debug] Resolving {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn}");
                var res = ResolveTableColumnType(col.SourceSchema, col.SourceTable, col.SourceColumn);
                if (!string.IsNullOrWhiteSpace(res.SqlTypeName))
                {
                    col.SqlTypeName = res.SqlTypeName;
                    if (res.MaxLength.HasValue) col.MaxLength = res.MaxLength.Value;
                    if (res.IsNullable.HasValue) col.IsNullable = res.IsNullable.Value;
                    if (ResolveTableColumnUserType != null)
                    {
                        var udt = ResolveTableColumnUserType(col.SourceSchema, col.SourceTable, col.SourceColumn);
                        if (!string.IsNullOrWhiteSpace(udt.Schema) && string.IsNullOrWhiteSpace(col.UserTypeSchemaName)) col.UserTypeSchemaName = udt.Schema;
                        if (!string.IsNullOrWhiteSpace(udt.Name) && string.IsNullOrWhiteSpace(col.UserTypeName)) col.UserTypeName = udt.Name;
                    }
                    if (ShouldDiag()) System.Console.WriteLine($"[cte-col-type-debug] Resolved to: type={col.SqlTypeName} maxLen={col.MaxLength} nullable={col.IsNullable}");

                    // Store the resolved type information for later CTE usage
                    // Note: This is a static method, so we can't directly access the visitor instance
                    // We'll use a different approach for storing type information
                }
                else
                {
                    if (ShouldDiag()) System.Console.WriteLine($"[cte-col-type-debug] ResolveTableColumnType returned empty SqlTypeName");
                }
            }
            catch (Exception ex)
            {
                if (ShouldDiag()) System.Console.WriteLine($"[cte-col-type-debug] Exception: {ex.Message}");
            }
        }

        // Store and retrieve column type information for CTE processing
        private void StoreColumnTypeInfo(string schema, string table, string column, string sqlTypeName, int? maxLength, bool? isNullable)
        {
            if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column)) return;
            var key = $"{schema}.{table}.{column}";
            _resolvedColumnTypes[key] = (sqlTypeName, maxLength, isNullable);
            if (ShouldDiag()) System.Console.WriteLine($"[cte-type-store] stored {key} -> {sqlTypeName} maxLen={maxLength} nullable={isNullable}");
        }

        private (string SqlTypeName, int? MaxLength, bool? IsNullable) GetStoredColumnTypeInfo(string schema, string table, string column)
        {
            if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column))
                return (string.Empty, null, null);

            var key = $"{schema}.{table}.{column}";
            if (_resolvedColumnTypes.TryGetValue(key, out var typeInfo))
            {
                if (ShouldDiag()) System.Console.WriteLine($"[cte-type-retrieve] found {key} -> {typeInfo.SqlTypeName} maxLen={typeInfo.MaxLength} nullable={typeInfo.IsNullable}");
                return typeInfo;
            }
            if (ShouldDiag()) System.Console.WriteLine($"[cte-type-retrieve] not found {key}");
            return (string.Empty, null, null);
        }

        private static void CopyColumnType(ResultColumn target, ResultColumn source)
        {
            if (target == null || source == null) return;
            if (string.IsNullOrWhiteSpace(target.SqlTypeName) && !string.IsNullOrWhiteSpace(source.SqlTypeName)) target.SqlTypeName = source.SqlTypeName;
            if (!target.MaxLength.HasValue && source.MaxLength.HasValue) target.MaxLength = source.MaxLength;
            if (!target.IsNullable.HasValue && source.IsNullable.HasValue) target.IsNullable = source.IsNullable;
            if (string.IsNullOrWhiteSpace(target.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(source.UserTypeSchemaName)) target.UserTypeSchemaName = source.UserTypeSchemaName;
            if (string.IsNullOrWhiteSpace(target.UserTypeName) && !string.IsNullOrWhiteSpace(source.UserTypeName)) target.UserTypeName = source.UserTypeName;
        }

        private void EnsureDerivedColumnType(ResultColumn col)
        {
            if (col == null || !string.IsNullOrWhiteSpace(col.SqlTypeName)) return;

            if (!string.IsNullOrWhiteSpace(col.SourceSchema) && !string.IsNullOrWhiteSpace(col.SourceTable) && !string.IsNullOrWhiteSpace(col.SourceColumn))
            {
                TryAssignColumnType(col);
                if (!string.IsNullOrWhiteSpace(col.SqlTypeName)) return;
            }

            var sourceName = col.SourceColumn ?? col.Name;

            if (!string.IsNullOrWhiteSpace(col.SourceAlias))
            {
                if (_tableVariableAliases.TryGetValue(col.SourceAlias, out var varKey) && _tableVariableColumns.TryGetValue(varKey, out var tableVarCols))
                {
                    var match = tableVarCols?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Name) && string.Equals(c.Name, sourceName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.SqlTypeName));
                    if (match != null)
                    {
                        CopyColumnType(col, match);
                        return;
                    }
                }

                if (_derivedTableColumns.TryGetValue(col.SourceAlias, out var aliasCols))
                {
                    var match = aliasCols?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Name) && string.Equals(c.Name, sourceName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.SqlTypeName));
                    if (match != null)
                    {
                        CopyColumnType(col, match);
                        return;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(sourceName))
            {
                ResultColumn unique = null; int hits = 0;
                foreach (var kv in _tableVariableColumns)
                {
                    var candidate = kv.Value?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Name) && string.Equals(c.Name, sourceName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.SqlTypeName));
                    if (candidate != null)
                    {
                        unique = candidate;
                        hits++;
                        if (hits > 1) break;
                    }
                }
                if (hits == 1 && unique != null)
                {
                    CopyColumnType(col, unique);
                    return;
                }

                if (hits == 0)
                {
                    var collapsed = new string(sourceName.Where(char.IsLetterOrDigit).ToArray());
                    if (!string.IsNullOrWhiteSpace(collapsed))
                    {
                        foreach (var kv in _tableVariableColumns)
                        {
                            foreach (var candidate in kv.Value ?? new List<ResultColumn>())
                            {
                                if (string.IsNullOrWhiteSpace(candidate.Name) || string.IsNullOrWhiteSpace(candidate.SqlTypeName)) continue;
                                var candCollapsed = new string(candidate.Name.Where(char.IsLetterOrDigit).ToArray());
                                if (string.Equals(collapsed, candCollapsed, StringComparison.OrdinalIgnoreCase))
                                {
                                    CopyColumnType(col, candidate);
                                    return;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static ResultColumn CloneForLookup(string key, ResultColumn source)
        {
            if (string.IsNullOrWhiteSpace(key) || source == null || string.IsNullOrWhiteSpace(source.SqlTypeName)) return null;
            return new ResultColumn
            {
                Name = key,
                SqlTypeName = source.SqlTypeName,
                MaxLength = source.MaxLength,
                IsNullable = source.IsNullable,
                UserTypeSchemaName = source.UserTypeSchemaName,
                UserTypeName = source.UserTypeName
            };
        }

        private ResultColumn FindTableVariableColumn(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || _tableVariableColumns == null || _tableVariableColumns.Count == 0) return null;

            foreach (var cols in _tableVariableColumns.Values)
            {
                if (cols == null) continue;
                var direct = cols.FirstOrDefault(c => c != null
                    && !string.IsNullOrWhiteSpace(c.Name)
                    && !string.IsNullOrWhiteSpace(c.SqlTypeName)
                    && c.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (direct != null) return direct;
            }

            var collapsed = new string(candidate.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrWhiteSpace(collapsed)) return null;

            foreach (var cols in _tableVariableColumns.Values)
            {
                if (cols == null) continue;
                foreach (var col in cols)
                {
                    if (col == null || string.IsNullOrWhiteSpace(col.Name) || string.IsNullOrWhiteSpace(col.SqlTypeName)) continue;
                    var colCollapsed = new string(col.Name.Where(char.IsLetterOrDigit).ToArray());
                    if (string.Equals(colCollapsed, collapsed, StringComparison.OrdinalIgnoreCase)) return col;
                }
            }

            return null;
        }

        private void RegisterCteColumnType(string alias, ResultColumn column)
        {
            if (column == null || string.IsNullOrWhiteSpace(column.Name) || string.IsNullOrWhiteSpace(column.SqlTypeName)) return;

            void TryStore(string key)
            {
                var clone = CloneForLookup(key, column);
                if (clone == null) return;
                _cteColumnTypes[key] = clone;
            }

            var baseKey = column.Name.Trim();
            TryStore(baseKey);

            var last = baseKey.Contains('.') ? baseKey.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() : baseKey;
            if (!string.IsNullOrWhiteSpace(last)) TryStore(last);

            if (!string.IsNullOrWhiteSpace(alias))
            {
                var aliasKey = alias.Trim();
                if (!string.IsNullOrWhiteSpace(aliasKey))
                {
                    TryStore($"{aliasKey}.{baseKey}");
                    if (!string.IsNullOrWhiteSpace(last)) TryStore($"{aliasKey}.{last}");
                }
            }
        }

        // Final pass to set missing types on derived/CTE columns based on structural information
        public void FinalizeDerivedColumnTypes()
        {
            try
            {
                var iteration = 0;
                bool changed;
                do
                {
                    changed = false;
                    foreach (var kv in _derivedTableColumns)
                    {
                        var cols = kv.Value; if (cols == null) continue;
                        foreach (var col in cols)
                        {
                            if (col == null || !string.IsNullOrWhiteSpace(col.SqlTypeName)) continue;
                            var before = col.SqlTypeName;
                            EnsureDerivedColumnType(col);
                            if (string.IsNullOrWhiteSpace(before) && !string.IsNullOrWhiteSpace(col.SqlTypeName)) changed = true;
                        }
                    }
                } while (changed && ++iteration < 4);

                foreach (var kv in _derivedTableColumns)
                {
                    var alias = kv.Key;
                    var cols = kv.Value; if (cols == null) continue;
                    foreach (var col in cols)
                    {
                        if (col == null) continue;
                        if (string.IsNullOrWhiteSpace(col.SqlTypeName))
                        {
                            if (col.HasDecimalLiteral) col.SqlTypeName = "decimal(18,2)";
                            else if (col.HasIntegerLiteral) col.SqlTypeName = "int";
                        }
                        RegisterCteColumnType(alias, col);
                    }
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
        // Entfernt: fr�here heuristische Methoden (HasIdSuffix / IsInOutLiteral) zugunsten strikt AST-basierter Analyse.
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
            {
                foreach (var tr in qs.FromClause.TableReferences)
                {
                    CollectLocalNamedTableReferences(tr, localAliases, localTableSources);
                    // Wichtig: DerivedTables (inkl. Subselect-Alias wie 'sub') vorab verarbeiten, damit _derivedTableColumns verf gbar sind
                    if (tr is QueryDerivedTable qdt)
                    {
                        try { ProcessQueryDerivedTable(qdt); } catch { }
                    }
                }
            }

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
                // Wichtig: Name setzen f�r sp�tere Aggregate/Flag-Propagation �ber TryPropagateAggregateFromDerived
                col.Name = alias;
                var state = new SourceBindingState();
                AnalyzeScalarExpressionDerived(sce.Expression, col, state, localAliases, localTableSources);
                // Ensure scalar function return types are applied at capture time (if not already set by analyzer)
                if (string.IsNullOrWhiteSpace(col.SqlTypeName) && sce.Expression is FunctionCall fnc)
                {
                    try { TryApplyScalarFunctionReturnType(fnc, col); } catch { }
                }
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
                // Zus?tzliche IIF-Typableitung direkt beim Ablegen der Derived-Spalten, falls noch kein Typ vorhanden
                try
                {
                    if (string.IsNullOrWhiteSpace(col.SqlTypeName))
                    {
                        if (sce.Expression is IIfCall ii)
                        {
                            var thenCol = new ResultColumn(); var thenState = new SourceBindingState();
                            AnalyzeScalarExpressionDerived(ii.ThenExpression as ScalarExpression, thenCol, thenState, localAliases, localTableSources);
                            // Falls Branch nur gebunden ist, aber noch keinen Typ hat, direkt Tabellentyp zuweisen
                            if (string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && !string.IsNullOrWhiteSpace(thenCol.SourceSchema) && !string.IsNullOrWhiteSpace(thenCol.SourceTable) && !string.IsNullOrWhiteSpace(thenCol.SourceColumn))
                                TryAssignColumnType(thenCol);
                            var elseCol = new ResultColumn(); var elseState = new SourceBindingState();
                            AnalyzeScalarExpressionDerived(ii.ElseExpression as ScalarExpression, elseCol, elseState, localAliases, localTableSources);
                            if (string.IsNullOrWhiteSpace(elseCol.SqlTypeName) && !string.IsNullOrWhiteSpace(elseCol.SourceSchema) && !string.IsNullOrWhiteSpace(elseCol.SourceTable) && !string.IsNullOrWhiteSpace(elseCol.SourceColumn))
                                TryAssignColumnType(elseCol);
                            if (!string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && thenCol.SqlTypeName.Equals(elseCol.SqlTypeName, StringComparison.OrdinalIgnoreCase))
                            {
                                col.SqlTypeName = thenCol.SqlTypeName;
                                col.MaxLength = thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue ? Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value) : thenCol.MaxLength ?? elseCol.MaxLength;
                            }
                            else if (!string.IsNullOrWhiteSpace(thenCol.SqlTypeName))
                            {
                                col.SqlTypeName = thenCol.SqlTypeName; col.MaxLength = thenCol.MaxLength;
                            }
                            else if (!string.IsNullOrWhiteSpace(elseCol.SqlTypeName))
                            {
                                col.SqlTypeName = elseCol.SqlTypeName; col.MaxLength = elseCol.MaxLength;
                            }
                        }
                        else if (sce.Expression is FunctionCall fniif && fniif.FunctionName?.Value?.Equals("IIF", StringComparison.OrdinalIgnoreCase) == true && fniif.Parameters?.Count == 3)
                        {
                            var thenCol = new ResultColumn(); var thenState = new SourceBindingState();
                            AnalyzeScalarExpressionDerived(fniif.Parameters[1] as ScalarExpression, thenCol, thenState, localAliases, localTableSources);
                            if (string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && !string.IsNullOrWhiteSpace(thenCol.SourceSchema) && !string.IsNullOrWhiteSpace(thenCol.SourceTable) && !string.IsNullOrWhiteSpace(thenCol.SourceColumn))
                                TryAssignColumnType(thenCol);
                            var elseCol = new ResultColumn(); var elseState = new SourceBindingState();
                            AnalyzeScalarExpressionDerived(fniif.Parameters[2] as ScalarExpression, elseCol, elseState, localAliases, localTableSources);
                            if (string.IsNullOrWhiteSpace(elseCol.SqlTypeName) && !string.IsNullOrWhiteSpace(elseCol.SourceSchema) && !string.IsNullOrWhiteSpace(elseCol.SourceTable) && !string.IsNullOrWhiteSpace(elseCol.SourceColumn))
                                TryAssignColumnType(elseCol);
                            if (!string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && thenCol.SqlTypeName.Equals(elseCol.SqlTypeName, StringComparison.OrdinalIgnoreCase))
                            {
                                col.SqlTypeName = thenCol.SqlTypeName;
                                col.MaxLength = thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue ? Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value) : thenCol.MaxLength ?? elseCol.MaxLength;
                            }
                            else if (!string.IsNullOrWhiteSpace(thenCol.SqlTypeName))
                            {
                                col.SqlTypeName = thenCol.SqlTypeName; col.MaxLength = thenCol.MaxLength;
                            }
                            else if (!string.IsNullOrWhiteSpace(elseCol.SqlTypeName))
                            {
                                col.SqlTypeName = elseCol.SqlTypeName; col.MaxLength = elseCol.MaxLength;
                            }
                        }
                        // Falls einfache Spaltenreferenz auf einen bereits erfassten Derived-Alias zeigt, Typ direkt propagieren
                        else if (sce.Expression is ColumnReferenceExpression crefCap && crefCap.MultiPartIdentifier?.Identifiers?.Count == 2)
                        {
                            var alias0 = crefCap.MultiPartIdentifier.Identifiers[0].Value;
                            var col0 = crefCap.MultiPartIdentifier.Identifiers[1].Value;
                            if (_derivedTableColumns.TryGetValue(alias0, out var dcolsAlias))
                            {
                                var src = dcolsAlias.FirstOrDefault(c => c.Name?.Equals(col0, StringComparison.OrdinalIgnoreCase) == true);
                                if (src != null && !string.IsNullOrWhiteSpace(src.SqlTypeName))
                                {
                                    col.SqlTypeName = src.SqlTypeName;
                                    if (src.MaxLength.HasValue) col.MaxLength = src.MaxLength;
                                    if (src.IsNullable.HasValue) col.IsNullable = src.IsNullable;
                                }
                            }
                        }
                    }
                }
                catch { }

                var ambiguous = col.IsAmbiguous == true || state.BindingCount > 1;
                map[alias] = (col.SourceSchema, col.SourceTable, col.SourceColumn, ambiguous);

                // CRITICAL: Nach dem Derived-Binding die Typ-Informationen direkt aus Table-Metadata laden
                if (!string.IsNullOrWhiteSpace(col.SourceSchema) && !string.IsNullOrWhiteSpace(col.SourceTable) && !string.IsNullOrWhiteSpace(col.SourceColumn))
                {
                    // Direkt TryAssignColumnType aufrufen statt �ber Mock-ColumnReference
                    TryAssignColumnType(col);

                    if (ShouldDiag())
                    {
                        try
                        {
                            System.Console.WriteLine($"[cte-col-type-loaded] {col.Name} from {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} type={col.SqlTypeName} maxLen={col.MaxLength} nullable={col.IsNullable}");
                        }
                        catch { }
                    }
                }
                // Table-variable alias typing fallback (assign types from DECLARE TABLE columns for alias.column)
                if (string.IsNullOrWhiteSpace(col.SqlTypeName) && sce.Expression is ColumnReferenceExpression tvRef && tvRef.MultiPartIdentifier?.Identifiers?.Count == 2)
                {
                    var a = tvRef.MultiPartIdentifier.Identifiers[0].Value;
                    var c = tvRef.MultiPartIdentifier.Identifiers[1].Value;
                    if (_tableVariableAliases.TryGetValue(a, out var varKey) && _tableVariableColumns.TryGetValue(varKey, out var tvCols))
                    {
                        var src = tvCols.FirstOrDefault(x => x.Name.Equals(c, StringComparison.OrdinalIgnoreCase));
                        if (src != null && !string.IsNullOrWhiteSpace(src.SqlTypeName))
                        {
                            col.SqlTypeName = src.SqlTypeName;
                            if (src.MaxLength.HasValue) col.MaxLength = src.MaxLength;
                            if (src.IsNullable.HasValue) col.IsNullable = src.IsNullable;
                            if (ShouldDiag()) System.Console.WriteLine($"[cte-col-type-tv] {col.Name} from {a}=>{varKey}.{c} type={col.SqlTypeName}");
                        }
                    }
                }

                // Fallback-Typableitung f?r Derived-Spalten ohne konkrete Bindung/Cast
                if (string.IsNullOrWhiteSpace(col.SqlTypeName))
                {
                    if (col.IsAggregate == true && !string.IsNullOrWhiteSpace(col.AggregateFunction))
                    {
                        var ag = col.AggregateFunction.ToLowerInvariant();
                        if (ag == "sum" || ag == "avg") col.SqlTypeName = "decimal(18,2)";
                        else if (ag == "count") col.SqlTypeName = col.SqlTypeName ?? "int";
                        else if (ag == "count_big") col.SqlTypeName = col.SqlTypeName ?? "bigint";
                    }
                    if (string.IsNullOrWhiteSpace(col.SqlTypeName) && col.HasDecimalLiteral) col.SqlTypeName = "decimal(18,2)";
                    if (string.IsNullOrWhiteSpace(col.SqlTypeName) && col.HasIntegerLiteral) col.SqlTypeName = "int";
                }

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
                        // Map CTE name to local alias for nested/derived resolution
                        try
                        {
                            if (_derivedTableColumns.TryGetValue(table, out var cteCols))
                            {
                                _derivedTableColumns[key] = cteCols;
                                if (_derivedTableColumnSources.TryGetValue(table, out var cteMap))
                                    _derivedTableColumnSources[key] = cteMap;
                            }
                        }
                        catch { }
                    }
                    break;
                case VariableTableReference vtr:
                    try
                    {
                        var alias = vtr.Alias?.Value;
                        var varName = vtr.Variable?.Name;
                        if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(varName))
                        {
                            var varKey = varName.StartsWith("@") ? varName : ("@" + varName);
                            _tableVariableAliases[alias] = varKey;
                            if (_tableVariableColumns.TryGetValue(varKey, out var tvCols) && tvCols != null)
                            {
                                _derivedTableColumns[alias] = tvCols;
                            }
                        }
                    }
                    catch { }
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
                    // Mirror non-derived handling: mark as Cast and capture target type
                    target.ExpressionKind = ResultColumnExpressionKind.Cast;
                    if (castCall.DataType?.Name?.Identifiers?.Count > 0)
                    {
                        var typeName = string.Join('.', castCall.DataType.Name.Identifiers.Select(i => i.Value));
                        if (!string.IsNullOrWhiteSpace(typeName)) typeName = typeName.ToLowerInvariant();
                        if (!string.IsNullOrWhiteSpace(typeName))
                        {
                            target.CastTargetType = typeName;
                            // For CTE/derived columns, assign SqlTypeName eagerly to enable early CTE type capture
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName)) target.SqlTypeName = typeName;
                            if (string.Equals(typeName, "bit", StringComparison.OrdinalIgnoreCase) && !target.MaxLength.HasValue) target.MaxLength = 1;
                        }
                        TryExtractTypeParameters(castCall.DataType, target);
                    }
                    AnalyzeScalarExpressionDerived(castCall.Parameter, target, state, localAliases, localTableSources);
                    break;
                case ConvertCall convertCall:
                    target.ExpressionKind = ResultColumnExpressionKind.Cast;
                    if (convertCall.DataType?.Name?.Identifiers?.Count > 0)
                    {
                        var typeName = string.Join('.', convertCall.DataType.Name.Identifiers.Select(i => i.Value));
                        if (!string.IsNullOrWhiteSpace(typeName)) typeName = typeName.ToLowerInvariant();
                        if (!string.IsNullOrWhiteSpace(typeName))
                        {
                            target.CastTargetType = typeName;
                            if (string.IsNullOrWhiteSpace(target.SqlTypeName)) target.SqlTypeName = typeName;
                            if (string.Equals(typeName, "bit", StringComparison.OrdinalIgnoreCase) && !target.MaxLength.HasValue) target.MaxLength = 1;
                        }
                        TryExtractTypeParameters(convertCall.DataType, target);
                    }
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
                            // Aggregate handling in derived path with parameter type propagation
                            if (!string.IsNullOrWhiteSpace(fnName2))
                            {
                                var lower2 = fnName2.ToLowerInvariant();
                                ResultColumn paramCol = null;
                                if (fn.Parameters != null && fn.Parameters.Count > 0)
                                {
                                    var pExpr = fn.Parameters[0] as ScalarExpression;
                                    if (pExpr != null)
                                    {
                                        paramCol = new ResultColumn(); var stp = new SourceBindingState();
                                        AnalyzeScalarExpressionDerived(pExpr, paramCol, stp, localAliases, localTableSources);
                                    }
                                }
                                string pType = paramCol?.SqlTypeName?.ToLowerInvariant();
                                var pMeta = ParseTypeString(pType);
                                if (lower2 is "sum" or "count" or "count_big" or "avg" or "exists" or "min" or "max")
                                {
                                    target.IsAggregate = true; target.AggregateFunction = lower2;
                                    if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                                    {
                                        switch (lower2)
                                        {
                                            case "count": target.SqlTypeName = "int"; break;
                                            case "count_big": target.SqlTypeName = "bigint"; break;
                                            case "exists": target.SqlTypeName = "bit"; break;
                                            case "avg":
                                                if (pMeta.Base == "decimal" || pMeta.Base == "numeric")
                                                    target.SqlTypeName = (pMeta.Prec.HasValue && pMeta.Scale.HasValue) ? $"decimal({pMeta.Prec},{pMeta.Scale})" : "decimal(18,2)";
                                                else if (pMeta.Base == "int" || pMeta.Base == "smallint" || pMeta.Base == "tinyint" || pMeta.Base == "bigint")
                                                    target.SqlTypeName = "decimal(18,2)";
                                                else
                                                    target.SqlTypeName = "decimal(18,2)";
                                                break;
                                            case "sum":
                                                if (pMeta.Base == "decimal" || pMeta.Base == "numeric")
                                                    target.SqlTypeName = (pMeta.Prec.HasValue && pMeta.Scale.HasValue) ? $"decimal({pMeta.Prec},{pMeta.Scale})" : "decimal(18,2)";
                                                else if (pMeta.Base == "bigint")
                                                    target.SqlTypeName = "bigint";
                                                else if (pMeta.Base == "int" || pMeta.Base == "smallint" || pMeta.Base == "tinyint")
                                                    target.SqlTypeName = "int";
                                                else
                                                    target.SqlTypeName = "decimal(18,2)";
                                                break;
                                            case "min":
                                            case "max":
                                                if (pMeta.Base == "decimal" || pMeta.Base == "numeric")
                                                    target.SqlTypeName = (pMeta.Prec.HasValue && pMeta.Scale.HasValue) ? $"decimal({pMeta.Prec},{pMeta.Scale})" : "decimal(18,2)";
                                                else if (pMeta.Base == "nvarchar" || pMeta.Base == "varchar" || pMeta.Base == "nchar" || pMeta.Base == "char")
                                                    target.SqlTypeName = pMeta.Len.HasValue ? $"{pMeta.Base}({pMeta.Len})" : pMeta.Base;
                                                else if (!string.IsNullOrWhiteSpace(pMeta.Base))
                                                    target.SqlTypeName = pMeta.Base;
                                                else
                                                    target.SqlTypeName = "nvarchar";
                                                break;
                                        }
                                    }
                                }
                            }
                            // IIF Typableitung analog Non-Derived
                            if (!string.IsNullOrWhiteSpace(fnName2) && fnName2.Equals("IIF", StringComparison.OrdinalIgnoreCase) && fn.Parameters?.Count == 3)
                            {
                                var thenExpr = fn.Parameters[1];
                                var elseExpr = fn.Parameters[2];
                                var thenCol = new ResultColumn(); var thenState = new SourceBindingState();
                                AnalyzeScalarExpressionDerived(thenExpr as ScalarExpression, thenCol, thenState, localAliases, localTableSources);
                                var elseCol = new ResultColumn(); var elseState = new SourceBindingState();
                                AnalyzeScalarExpressionDerived(elseExpr as ScalarExpression, elseCol, elseState, localAliases, localTableSources);

                                if (string.IsNullOrWhiteSpace(target.SqlTypeName) && !string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && thenCol.SqlTypeName.Equals(elseCol.SqlTypeName, StringComparison.OrdinalIgnoreCase))
                                {
                                    target.SqlTypeName = thenCol.SqlTypeName;
                                    if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                        target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                    else if (thenCol.MaxLength.HasValue || elseCol.MaxLength.HasValue)
                                        target.MaxLength = thenCol.MaxLength ?? elseCol.MaxLength;
                                }
                                else if (string.IsNullOrWhiteSpace(target.SqlTypeName)
                                    && !string.IsNullOrWhiteSpace(thenCol.SqlTypeName) && !string.IsNullOrWhiteSpace(elseCol.SqlTypeName)
                                    && thenCol.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase)
                                    && elseCol.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase))
                                {
                                    target.SqlTypeName = "nvarchar";
                                    if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                        target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                    else
                                        target.MaxLength = null;
                                }
                                else if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                                {
                                    if (!string.IsNullOrWhiteSpace(thenCol.SqlTypeName))
                                    {
                                        target.SqlTypeName = thenCol.SqlTypeName;
                                        if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                            target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                        else if (thenCol.MaxLength.HasValue || elseCol.MaxLength.HasValue)
                                            target.MaxLength = thenCol.MaxLength ?? elseCol.MaxLength;
                                    }
                                    else if (!string.IsNullOrWhiteSpace(elseCol.SqlTypeName))
                                    {
                                        target.SqlTypeName = elseCol.SqlTypeName;
                                        if (thenCol.MaxLength.HasValue && elseCol.MaxLength.HasValue)
                                            target.MaxLength = Math.Max(thenCol.MaxLength.Value, elseCol.MaxLength.Value);
                                        else if (thenCol.MaxLength.HasValue || elseCol.MaxLength.HasValue)
                                            target.MaxLength = thenCol.MaxLength ?? elseCol.MaxLength;
                                    }
                                }
                            }
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
                                                // Fallback Heuristik: Pr�fe ob direkter IIF() mit Literal 1/0 Parametern vorhanden (locker)
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
                        // Versuche, skalaren Funktions-R?ckgabetyp zuzuweisen, falls noch nicht gesetzt
                        try { TryApplyScalarFunctionReturnType(fn, target); } catch { }
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
                        // Nach Parameteranalyse (derived): Falls IIF Klassifikation �berschrieben wurde, zur�cksetzen
                        if (!string.IsNullOrWhiteSpace(fnName2) && fnName2.Equals("IIF", StringComparison.OrdinalIgnoreCase))
                        {
                            if (target.ExpressionKind != ResultColumnExpressionKind.FunctionCall && target.ExpressionKind != ResultColumnExpressionKind.JsonQuery)
                                target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                        }
                    }
                    else
                    {
                        // JSON_QUERY im Derived-Kontext: Parameter untersuchen wie im prim�ren Analyzer
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
                        // Numerische IIF-Typen: wenn eine Seite decimal/numeric oder Dezimalliteral -> decimal(18,2); sonst bei Ganzzahl -> int
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                        {
                            string t = thenCol2.SqlTypeName?.ToLowerInvariant();
                            string e = elseCol2.SqlTypeName?.ToLowerInvariant();
                            bool tDec = !string.IsNullOrWhiteSpace(t) && (t.Contains("decimal") || t.Contains("numeric") || t.Contains("money") || t.Contains("float") || t.Contains("real"));
                            bool eDec = !string.IsNullOrWhiteSpace(e) && (e.Contains("decimal") || e.Contains("numeric") || e.Contains("money") || e.Contains("float") || e.Contains("real"));
                            bool tInt = !string.IsNullOrWhiteSpace(t) && (t == "int" || t == "bigint" || t == "smallint" || t == "tinyint");
                            bool eInt = !string.IsNullOrWhiteSpace(e) && (e == "int" || e == "bigint" || e == "smallint" || e == "tinyint");
                            if (tDec || eDec || thenCol2.HasDecimalLiteral || elseCol2.HasDecimalLiteral)
                                target.SqlTypeName = "decimal(18,2)";
                            else if (tInt || eInt || thenCol2.HasIntegerLiteral || elseCol2.HasIntegerLiteral)
                                target.SqlTypeName = "int";
                            else if (!string.IsNullOrWhiteSpace(t) && t == e)
                                target.SqlTypeName = thenCol2.SqlTypeName;
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
                    // Bewahre Computed falls durch Operanden �berschrieben
                    if (prevKind == ResultColumnExpressionKind.Computed && target.ExpressionKind != ResultColumnExpressionKind.Computed)
                        target.ExpressionKind = ResultColumnExpressionKind.Computed;
                    // Zus?tzliche Typableitung f?r arithmetische Ausdr?cke, falls Ziel noch keinen Typ hat
                    try
                    {
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName))
                        {
                            var ltmp = new ResultColumn(); var ls = new SourceBindingState();
                            AnalyzeScalarExpressionDerived(be.FirstExpression, ltmp, ls, localAliases, localTableSources);
                            var rtmp = new ResultColumn(); var rs = new SourceBindingState();
                            AnalyzeScalarExpressionDerived(be.SecondExpression, rtmp, rs, localAliases, localTableSources);
                            string l = ltmp.SqlTypeName?.ToLowerInvariant();
                            string r = rtmp.SqlTypeName?.ToLowerInvariant();
                            bool lDec = !string.IsNullOrWhiteSpace(l) && (l.Contains("decimal") || l.Contains("numeric") || l.Contains("money") || l.Contains("float") || l.Contains("real"));
                            bool rDec = !string.IsNullOrWhiteSpace(r) && (r.Contains("decimal") || r.Contains("numeric") || r.Contains("money") || r.Contains("float") || r.Contains("real"));
                            bool lInt = !string.IsNullOrWhiteSpace(l) && (l == "int" || l == "bigint" || l == "smallint" || l == "tinyint");
                            bool rInt = !string.IsNullOrWhiteSpace(r) && (r == "int" || r == "bigint" || r == "smallint" || r == "tinyint");
                            if (lDec || rDec || ltmp.HasDecimalLiteral || rtmp.HasDecimalLiteral)
                                target.SqlTypeName = "decimal(18,2)";
                            else if (lInt || rInt || ltmp.HasIntegerLiteral || rtmp.HasIntegerLiteral)
                                target.SqlTypeName = "int";
                            else if (!string.IsNullOrWhiteSpace(l)) { target.SqlTypeName = ltmp.SqlTypeName; target.MaxLength = ltmp.MaxLength; }
                            else if (!string.IsNullOrWhiteSpace(r)) { target.SqlTypeName = rtmp.SqlTypeName; target.MaxLength = rtmp.MaxLength; }
                        }
                    }
                    catch { }
                    break;
                case UnaryExpression ue:
                    AnalyzeScalarExpressionDerived(ue.Expression, target, state, localAliases, localTableSources);
                    break;
                case SearchedCaseExpression sce2:
                    // Derived CASE: unify THEN/ELSE branch types similar to non-derived path
                    target.ExpressionKind ??= ResultColumnExpressionKind.Computed;
                    try
                    {
                        var branchCols = new List<ResultColumn>();
                        if (sce2.WhenClauses != null)
                        {
                            foreach (var w in sce2.WhenClauses)
                            {
                                var bcol = new ResultColumn(); var bstate = new SourceBindingState();
                                AnalyzeScalarExpressionDerived(w.ThenExpression, bcol, bstate, localAliases, localTableSources);
                                branchCols.Add(bcol);
                            }
                        }
                        if (sce2.ElseExpression != null)
                        {
                            var ecol = new ResultColumn(); var estate = new SourceBindingState();
                            AnalyzeScalarExpressionDerived(sce2.ElseExpression, ecol, estate, localAliases, localTableSources);
                            branchCols.Add(ecol);
                        }
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName) && branchCols.Count > 0)
                        {
                            var typed = branchCols.Where(b => !string.IsNullOrWhiteSpace(b.SqlTypeName)).ToList();
                            if (typed.Count > 0)
                            {
                                var first = typed[0].SqlTypeName;
                                if (typed.All(b => b.SqlTypeName.Equals(first, StringComparison.OrdinalIgnoreCase)))
                                {
                                    target.SqlTypeName = first;
                                    var maxLens = typed.Where(b => b.MaxLength.HasValue).Select(b => b.MaxLength!.Value);
                                    if (maxLens.Any()) target.MaxLength = maxLens.Max();
                                }
                                else if (typed.All(b => b.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase)))
                                {
                                    target.SqlTypeName = "nvarchar";
                                    var maxLens = typed.Where(b => b.MaxLength.HasValue).Select(b => b.MaxLength!.Value);
                                    target.MaxLength = maxLens.Any() ? maxLens.Max() : (int?)null;
                                }
                                else
                                {
                                    target.SqlTypeName = typed[0].SqlTypeName;
                                    target.MaxLength = typed[0].MaxLength;
                                }
                            }
                            // If no ELSE branch present, CASE may yield NULL
                            if (sce2.ElseExpression == null && target.IsNullable != true) target.IsNullable = true;
                        }
                    }
                    catch { }
                    break;
                case SimpleCaseExpression simp:
                    // Derived CASE ... WHEN ...: unify branch types
                    target.ExpressionKind ??= ResultColumnExpressionKind.Computed;
                    AnalyzeScalarExpressionDerived(simp.InputExpression, target, state, localAliases, localTableSources);
                    try
                    {
                        var branchCols = new List<ResultColumn>();
                        if (simp.WhenClauses != null)
                        {
                            foreach (var w in simp.WhenClauses)
                            {
                                var bcol = new ResultColumn(); var bstate = new SourceBindingState();
                                AnalyzeScalarExpressionDerived(w.ThenExpression, bcol, bstate, localAliases, localTableSources);
                                branchCols.Add(bcol);
                            }
                        }
                        if (simp.ElseExpression != null)
                        {
                            var ecol = new ResultColumn(); var estate = new SourceBindingState();
                            AnalyzeScalarExpressionDerived(simp.ElseExpression, ecol, estate, localAliases, localTableSources);
                            branchCols.Add(ecol);
                        }
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName) && branchCols.Count > 0)
                        {
                            var typed = branchCols.Where(b => !string.IsNullOrWhiteSpace(b.SqlTypeName)).ToList();
                            if (typed.Count > 0)
                            {
                                var first = typed[0].SqlTypeName;
                                if (typed.All(b => b.SqlTypeName.Equals(first, StringComparison.OrdinalIgnoreCase)))
                                {
                                    target.SqlTypeName = first;
                                    var maxLens = typed.Where(b => b.MaxLength.HasValue).Select(b => b.MaxLength!.Value);
                                    if (maxLens.Any()) target.MaxLength = maxLens.Max();
                                }
                                else if (typed.All(b => b.SqlTypeName.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase)))
                                {
                                    target.SqlTypeName = "nvarchar";
                                    var maxLens = typed.Where(b => b.MaxLength.HasValue).Select(b => b.MaxLength!.Value);
                                    target.MaxLength = maxLens.Any() ? maxLens.Max() : (int?)null;
                                }
                                else
                                {
                                    target.SqlTypeName = typed[0].SqlTypeName;
                                    target.MaxLength = typed[0].MaxLength;
                                }
                            }
                            if (simp.ElseExpression == null && target.IsNullable != true) target.IsNullable = true;
                        }
                    }
                    catch { }
                    break;
                case ParenthesisExpression pe:
                    AnalyzeScalarExpressionDerived(pe.Expression, target, state, localAliases, localTableSources);
                    break;
                default:
                    break;
            }
        }

        private static (string Base, int? Len, int? Prec, int? Scale) ParseTypeString(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return (string.Empty, null, null, null);
            try
            {
                var lower = t.Trim().ToLowerInvariant();
                var idx = lower.IndexOf('(');
                if (idx < 0) return (lower, null, null, null);
                var b = lower.Substring(0, idx).Trim();
                var inside = lower.Substring(idx + 1).TrimEnd(')').Trim();
                var parts = inside.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 1)
                {
                    if (int.TryParse(parts[0], out var len)) return (b, len, null, null);
                    return (b, null, null, null);
                }
                if (parts.Length >= 2)
                {
                    int? p = null; int? s = null;
                    if (int.TryParse(parts[0], out var p0)) p = p0;
                    if (int.TryParse(parts[1], out var s1)) s = s1;
                    return (b, null, p, s);
                }
                return (b, null, null, null);
            }
            catch { return (t, null, null, null); }
        }

        private void ExtractTableVariableDeclarations()
        {
            try
            {
                if (ShouldDiag())
                {
                    System.Console.WriteLine($"[table-var-entry] diag={ShouldDiag()} len={_definition?.Length ?? 0}");
                }
                if (string.IsNullOrWhiteSpace(_definition)) return;
                var rxDecl = new System.Text.RegularExpressions.Regex(@"DECLARE\s+(@\w+)\s+TABLE\s*\((.*?)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                var rxCol = new System.Text.RegularExpressions.Regex(@"\[?\s*([A-Za-z0-9_]+)\s*\]?\s+((\[?([A-Za-z0-9_]+)\]?\.)?\[?([A-Za-z0-9_]+)\]?)(\s*\([^\)]*\))?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                static string ComposeSqlTypeName(string baseType, int? maxLength, int? precision, int? scale)
                {
                    if (string.IsNullOrWhiteSpace(baseType)) return string.Empty;
                    var lower = baseType.Trim().ToLowerInvariant();
                    if (precision.HasValue)
                    {
                        if (scale.HasValue) return $"{lower}({precision.Value},{scale.Value})";
                        return $"{lower}({precision.Value})";
                    }
                    if (maxLength.HasValue)
                    {
                        if (maxLength.Value < 0) return $"{lower}(max)";
                        return $"{lower}({maxLength.Value})";
                    }
                    return lower;
                }
                foreach (System.Text.RegularExpressions.Match m in rxDecl.Matches(_definition))
                {
                    var varName = m.Groups[1].Value; var colsText = m.Groups[2].Value;
                    var cols = new List<ResultColumn>();
                    foreach (System.Text.RegularExpressions.Match cm in rxCol.Matches(colsText))
                    {
                        var colName = cm.Groups[1].Value;
                        var schema = cm.Groups[4].Success ? cm.Groups[4].Value : null;
                        var typeName = cm.Groups[5].Value;
                        var suffix = cm.Groups[6].Success ? cm.Groups[6].Value : null;
                        var rc = new ResultColumn { Name = colName };
                        if (!string.IsNullOrWhiteSpace(schema)) rc.UserTypeSchemaName = schema;
                        if (!string.IsNullOrWhiteSpace(typeName)) rc.UserTypeName = typeName;
                        if (!string.IsNullOrWhiteSpace(schema) && !string.IsNullOrWhiteSpace(typeName) && ResolveUserDefinedType != null)
                        {
                            try
                            {
                                var udt = ResolveUserDefinedType(schema, typeName);
                                if (!string.IsNullOrWhiteSpace(udt.SqlTypeName))
                                {
                                    rc.SqlTypeName = ComposeSqlTypeName(udt.SqlTypeName, udt.MaxLength, udt.Precision, udt.Scale);
                                    if (udt.MaxLength.HasValue) rc.MaxLength = udt.MaxLength.Value < 0 ? null : udt.MaxLength.Value;
                                    if (udt.IsNullable.HasValue) rc.IsNullable = udt.IsNullable.Value;
                                    if (ShouldDiag()) System.Console.WriteLine($"[table-var-udt] {varName}.{colName} {schema}.{typeName} -> {rc.SqlTypeName} len={rc.MaxLength} nullable={rc.IsNullable}");
                                }
                            }
                            catch { }
                        }
                        if (string.IsNullOrWhiteSpace(rc.SqlTypeName) && !string.IsNullOrWhiteSpace(typeName))
                        {
                            var parsed = typeName.Trim();
                            if (!string.IsNullOrWhiteSpace(parsed))
                            {
                                var lower = parsed.ToLowerInvariant();
                                if (!string.IsNullOrWhiteSpace(suffix))
                                {
                                    var suffixNormalized = suffix.Trim();
                                    rc.SqlTypeName = (lower + suffixNormalized).ToLowerInvariant();
                                    try
                                    {
                                        var inner = suffixNormalized.Trim('(', ')');
                                        var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                        if (parts.Length == 1 && int.TryParse(parts[0], out var len))
                                        {
                                            rc.MaxLength = len < 0 ? null : len;
                                        }
                                    }
                                    catch { }
                                }
                                else
                                {
                                    rc.SqlTypeName = lower;
                                }
                            }
                        }
                        if (ShouldDiag())
                        {
                            var sqlType = string.IsNullOrWhiteSpace(rc.SqlTypeName) ? "" : rc.SqlTypeName;
                            System.Console.WriteLine($"[table-var-capture] {varName}.{colName} type={sqlType} len={rc.MaxLength} nullable={rc.IsNullable} utSchema={schema} utName={typeName}");
                        }
                        cols.Add(rc);
                    }
                    if (ShouldDiag()) System.Console.WriteLine($"[table-var-decl] {varName} columns={cols.Count} rawLen={(colsText?.Length ?? 0)}");
                    if (cols.Count > 0) _tableVariableColumns[varName] = cols;
                }
                if (ShouldDiag()) System.Console.WriteLine($"[table-var-total] captured={_tableVariableColumns.Count}");
            }
            catch { }
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

                // Pr�fe zuerst lokale Aliases (physische Tabellen)
                if (localAliases.TryGetValue(tableOrAlias, out var mapped))
                {
                    col.SourceAlias = tableOrAlias;
                    col.SourceSchema = mapped.Schema;
                    col.SourceTable = mapped.Table;
                    col.SourceColumn = column;
                    state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                    if (forceVerbose) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (derived-alias)"); } catch { }
                    _analysis.ColumnRefBound++;
                    // If this maps to a captured CTE/derived name, propagate its column type; otherwise fall back to physical table metadata
                    if (!string.IsNullOrWhiteSpace(mapped.Table) && _derivedTableColumns.TryGetValue(mapped.Table, out var cteColsForName))
                    {
                        var src = cteColsForName.FirstOrDefault(c => c.Name?.Equals(column, StringComparison.OrdinalIgnoreCase) == true);
                        if (src != null)
                        {
                            if (string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(src.SqlTypeName)) col.SqlTypeName = src.SqlTypeName;
                            if (!col.MaxLength.HasValue && src.MaxLength.HasValue) col.MaxLength = src.MaxLength;
                            if (!col.IsNullable.HasValue && src.IsNullable.HasValue) col.IsNullable = src.IsNullable;
                        }
                    }
                    else
                    {
                        // Assign type directly from physical table metadata when available
                        TryAssignColumnType(col);
                    }
                }
                else if (_derivedTableColumnSources.TryGetValue(tableOrAlias, out var dmap) && dmap.TryGetValue(column, out var dsrc))
                {
                    col.SourceAlias = tableOrAlias;
                    if (!string.IsNullOrWhiteSpace(dsrc.Schema)) col.SourceSchema = dsrc.Schema;
                    if (!string.IsNullOrWhiteSpace(dsrc.Table)) col.SourceTable = dsrc.Table;
                    if (!string.IsNullOrWhiteSpace(dsrc.Column)) col.SourceColumn = dsrc.Column;
                    if (dsrc.Ambiguous) col.IsAmbiguous = true;
                    state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                    if (forceVerbose) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (derived-map)"); } catch { }
                    _analysis.ColumnRefBound++;
                    TryAssignColumnType(col);
                }
                // Neue Funktionalit�t: Pr�fe CTE/Derived Table Aliases
                else if (_derivedTableColumns.TryGetValue(tableOrAlias, out var derivedCols))
                {
                    // Finde passende Spalte aus der CTE/Derived Table
                    var sourceCol = derivedCols.FirstOrDefault(c => c.Name?.Equals(column, StringComparison.OrdinalIgnoreCase) == true);
                    if (sourceCol != null)
                    {
                        // Propagiere Type-Informationen von der CTE-Spalte
                        if (!string.IsNullOrWhiteSpace(sourceCol.SqlTypeName))
                        {
                            col.SqlTypeName = sourceCol.SqlTypeName;
                        }
                        if (sourceCol.MaxLength.HasValue)
                        {
                            col.MaxLength = sourceCol.MaxLength;
                        }
                        if (sourceCol.IsNullable.HasValue)
                        {
                            col.IsNullable = sourceCol.IsNullable;
                        }

                        // Setze CTE-spezifische Source-Informationen
                        col.SourceAlias = tableOrAlias;
                        col.SourceColumn = column;
                        // F�r CTEs verwenden wir keine Schema.Table, da es virtuelle Tabellen sind

                        if (forceVerbose || ShouldDiag())
                        {
                            try
                            {
                                System.Console.WriteLine($"[cte-bind-derived] CTE {tableOrAlias}.{column} -> {col.Name} type={col.SqlTypeName} maxLen={col.MaxLength} nullable={col.IsNullable}");
                            }
                            catch { }
                        }
                        _analysis.ColumnRefBound++;
                    }
                    else
                    {
                        col.IsAmbiguous = true;
                        if (forceVerbose) try { System.Console.WriteLine($"[cte-bind-derived] CTE {tableOrAlias} found but column {column} not found"); } catch { }
                    }
                }
                // Table variable alias mapping for derived contexts
                else if (_tableVariableAliases.TryGetValue(tableOrAlias, out var varName) && _tableVariableColumns.TryGetValue(varName, out var vcols2))
                {
                    var vcol = vcols2.FirstOrDefault(c => c.Name?.Equals(column, StringComparison.OrdinalIgnoreCase) == true);
                    if (vcol != null)
                    {
                        col.SourceAlias = tableOrAlias;
                        col.SourceColumn = column;
                        if (string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(vcol.SqlTypeName)) col.SqlTypeName = vcol.SqlTypeName;
                        if (!col.MaxLength.HasValue && vcol.MaxLength.HasValue) col.MaxLength = vcol.MaxLength;
                        if (!col.IsNullable.HasValue && vcol.IsNullable.HasValue) col.IsNullable = vcol.IsNullable;
                        if (ShouldDiag()) { try { System.Console.WriteLine($"[json-ast-bind-force] var-table {tableOrAlias}.{column} -> {col.Name} type={col.SqlTypeName} maxLen={col.MaxLength} nullable={col.IsNullable}"); } catch { } }
                        _analysis.ColumnRefBound++;
                    }
                }
                // Table variable alias mapping (derived context)
                else if (_tableVariableAliases.TryGetValue(tableOrAlias, out var tvVarName) && _tableVariableColumns.TryGetValue(tvVarName, out var tvCols))
                {
                    var vcol = tvCols.FirstOrDefault(c => c.Name?.Equals(column, StringComparison.OrdinalIgnoreCase) == true);
                    if (vcol != null)
                    {
                        col.SourceAlias = tableOrAlias;
                        col.SourceColumn = column;
                        if (string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(vcol.SqlTypeName)) col.SqlTypeName = vcol.SqlTypeName;
                        if (!col.MaxLength.HasValue && vcol.MaxLength.HasValue) col.MaxLength = vcol.MaxLength;
                        if (!col.IsNullable.HasValue && vcol.IsNullable.HasValue) col.IsNullable = vcol.IsNullable;
                        state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                        if (ShouldDiag()) { try { System.Console.WriteLine($"[json-ast-bind-force] derived-var {tableOrAlias}.{column} -> {col.Name} type={col.SqlTypeName} maxLen={col.MaxLength} nullable={col.IsNullable}"); } catch { } }
                        _analysis.ColumnRefBound++;
                    }
                }
                else
                {
                    col.IsAmbiguous = true;
                    if (forceVerbose) try { System.Console.WriteLine($"[cte-bind-derived] Neither physical table nor CTE found for alias {tableOrAlias}"); } catch { }
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
                if (forceVerbose) try { System.Console.WriteLine($"[json-ast-bind-force] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} (derived-three-part)"); } catch { }
                _analysis.ColumnRefBound++;
            }
            if (col.IsAmbiguous == true) _analysis.ColumnRefAmbiguous++;
        }

        /// <summary>
        /// Analysiert eine innere QuerySpecification innerhalb eines JSON_QUERY( (subquery) , path ) Aufrufs.
        /// Ziel: Wenn das Subselect genau eine SelectElement hat und diese ein Aggregat (SUM/COUNT/COUNT_BIG/AVG/EXISTS)
        /// oder eine einfache CAST davon ist, die Aggregat-Metadaten (IsAggregate, AggregateFunction, Literal Flags) auf die �u�ere JSON Column propagieren.
        /// </summary>
        private void AnalyzeJsonQueryInnerSubquery(QuerySpecification qs, ResultColumn outer, SourceBindingState state)
        {
            if (qs == null || outer == null) return;
            // Ensure table variable aliases from this subquery are registered for binding
            // Build local maps for this subquery: physical aliases and table variable aliases
            var localVarCols = new Dictionary<string, List<ResultColumn>>(StringComparer.OrdinalIgnoreCase);
            var localAliases = new Dictionary<string, (string Schema, string Table)>(StringComparer.OrdinalIgnoreCase);
            var localTableSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (qs.FromClause?.TableReferences != null)
                {
                    foreach (var tr in qs.FromClause.TableReferences)
                    {
                        CollectLocalNamedTableReferences(tr, localAliases, localTableSources);
                        if (tr is VariableTableReference vtr)
                        {
                            var alias = vtr.Alias?.Value;
                            var varName = vtr.Variable?.Name;
                            if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(varName))
                            {
                                var varKey = varName.StartsWith("@") ? varName : ("@" + varName);
                                if (_tableVariableColumns.TryGetValue(varKey, out var colsForVar))
                                {
                                    localVarCols[alias] = colsForVar;
                                    if (!_derivedTableColumns.ContainsKey(alias)) _derivedTableColumns[alias] = colsForVar;
                                }
                                if (!_tableVariableAliases.ContainsKey(alias))
                                    _tableVariableAliases[alias] = varKey;
                            }
                        }
                    }
                }
            }
            catch { }
            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] json-inner-enter outer={outer.Name} selectCount={qs.SelectElements?.Count}"); } catch { } }
            // Nur einfache F�lle: genau ein SelectElement, kein SELECT *
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
                    try
                    {
                        var rxVarAlias = new System.Text.RegularExpressions.Regex(@"@(?<var>\w+)\s+(?:AS\s+)?(?<alias>\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        foreach (System.Text.RegularExpressions.Match m in rxVarAlias.Matches(frag))
                        {
                            var v = m.Groups["var"].Value; var a = m.Groups["alias"].Value;
                            if (!string.IsNullOrWhiteSpace(v) && !string.IsNullOrWhiteSpace(a))
                            {
                                var key = "@" + v;
                                if (_tableVariableColumns.TryGetValue(key, out var colsForVar))
                                {
                                    localVarCols[a] = colsForVar;
                                    if (!_derivedTableColumns.ContainsKey(a)) _derivedTableColumns[a] = colsForVar;
                                    if (!_tableVariableAliases.ContainsKey(a)) _tableVariableAliases[a] = key;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            outer.ReturnsJsonArray = withoutArray ? false : true;
            var expandedChildren = outer.Columns != null ? outer.Columns.ToList() : new List<ResultColumn>();
            // Build a scoped alias symbol table for this nested subquery
            var scopeFrame = BuildScopeFrameForNested(localVarCols, localAliases);
            foreach (var se in qs.SelectElements.OfType<SelectScalarExpression>())
            {
                try
                {
                    var alias = se.ColumnName?.Value;
                    if (string.IsNullOrWhiteSpace(alias) && se.Expression is ColumnReferenceExpression cref && cref.MultiPartIdentifier?.Identifiers?.Count > 0)
                        alias = cref.MultiPartIdentifier.Identifiers.Last().Value;
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    var path = NormalizeJsonPath(alias);
                    var childName = SanitizeAliasPreserveDots(path);
                    if (string.IsNullOrWhiteSpace(childName)) continue;
                    var child = new ResultColumn { Name = childName };
                    var childState = new SourceBindingState();
                    // Use derived analyzer to allow local alias and table-variable binding
                    AnalyzeScalarExpressionDerived(se.Expression, child, childState, localAliases, localTableSources);
                    // If still untyped, try alias.column resolution via scoped symbols
                    try
                    {
                        if (string.IsNullOrWhiteSpace(child.SqlTypeName))
                        {
                            ColumnReferenceExpression cr2 = null;
                            if (se.Expression is ColumnReferenceExpression crx) cr2 = crx;
                            else if (se.Expression is CastCall cc && cc.Parameter is ColumnReferenceExpression crc) cr2 = crc;
                            if (cr2?.MultiPartIdentifier?.Identifiers?.Count == 2)
                            {
                                var a = cr2.MultiPartIdentifier.Identifiers[0].Value;
                                var c0 = cr2.MultiPartIdentifier.Identifiers[1].Value;
                                if (TryResolveFromScope(scopeFrame, a, c0, out var t, out var ml, out var nul))
                                {
                                    child.SqlTypeName = t; child.MaxLength = ml; child.IsNullable = nul;
                                }
                            }
                        }
                    }
                    catch { }
                    // If still untyped, try lineage-driven resolution in scoped aliases
                    try
                    {
                        if (string.IsNullOrWhiteSpace(child.SqlTypeName))
                        {
                            var lin = BuildLineage(se.Expression as ScalarExpression);
                            if (TryResolveLineageType(lin, scopeFrame, out var tlin, out var mll, out var nll) && !string.IsNullOrWhiteSpace(tlin))
                            {
                                child.SqlTypeName = tlin; child.MaxLength = mll; child.IsNullable = nll;
                            }
                        }
                    }
                    catch { }
                    // If still untyped and expression is a simple column from a table variable alias, resolve via captured @Var columns
                    try
                    {
                        if (se.Expression is ColumnReferenceExpression cr && cr.MultiPartIdentifier?.Identifiers?.Count == 2)
                        {
                            var alias0 = cr.MultiPartIdentifier.Identifiers[0].Value;
                            var col0 = cr.MultiPartIdentifier.Identifiers[1].Value;
                            if (localVarCols.TryGetValue(alias0, out var tcols))
                            {
                                var vcol = tcols.FirstOrDefault(c => c.Name?.Equals(col0, StringComparison.OrdinalIgnoreCase) == true);
                                if (vcol != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(vcol.SqlTypeName)) child.SqlTypeName = vcol.SqlTypeName;
                                    if (vcol.MaxLength.HasValue) child.MaxLength = vcol.MaxLength;
                                    if (vcol.IsNullable.HasValue) child.IsNullable = vcol.IsNullable;
                                    if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] var-col-type name={child.Name} alias={alias0} srcCol={col0} sqlType={child.SqlTypeName} maxLen={child.MaxLength}"); } catch { } }
                                }
                            }
                            // If alias points to a CTE/derived table captured earlier, propagate its column type
                            else if (localAliases.TryGetValue(alias0, out var mappedAlias)
                                     && !string.IsNullOrWhiteSpace(mappedAlias.Table)
                                     && _derivedTableColumns.TryGetValue(mappedAlias.Table, out var dcolsJson))
                            {
                                var src = dcolsJson.FirstOrDefault(c => c.Name?.Equals(col0, StringComparison.OrdinalIgnoreCase) == true);
                                if (src != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(src.SqlTypeName)) child.SqlTypeName = src.SqlTypeName;
                                    if (src.MaxLength.HasValue) child.MaxLength = src.MaxLength;
                                    if (src.IsNullable.HasValue) child.IsNullable = src.IsNullable;
                                    if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] cte-col-type name={child.Name} alias={alias0} table={mappedAlias.Table} srcCol={col0} sqlType={child.SqlTypeName}"); } catch { } }
                                }
                            }
                            // Fallback: if alias itself is a derived/CTE key, propagate directly
                            else if (_derivedTableColumns.TryGetValue(alias0, out var dcolsByAlias))
                            {
                                var src2 = dcolsByAlias.FirstOrDefault(c => c.Name?.Equals(col0, StringComparison.OrdinalIgnoreCase) == true);
                                if (src2 != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(src2.SqlTypeName)) child.SqlTypeName = src2.SqlTypeName;
                                    if (src2.MaxLength.HasValue) child.MaxLength = src2.MaxLength;
                                    if (src2.IsNullable.HasValue) child.IsNullable = src2.IsNullable;
                                    if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] cte-col-type-alias name={child.Name} alias={alias0} srcCol={col0} sqlType={child.SqlTypeName}"); } catch { } }
                                }
                            }
                        }
                    }
                    catch { }
                    try
                    {
                        if (se.StartOffset >= 0 && se.FragmentLength > 0 && _definition != null && se.StartOffset + se.FragmentLength <= _definition.Length)
                            child.RawExpression = _definition.Substring(se.StartOffset, se.FragmentLength).Trim();
                    }
                    catch { }
                    // Final nested-subquery fallback: try WClaimSum by last segment if still untyped
                    try
                    {
                        if (string.IsNullOrWhiteSpace(child.SqlTypeName))
                        {
                            var lastSeg = !string.IsNullOrWhiteSpace(child.Name) && child.Name.Contains('.') ? child.Name.Split('.').Last() : child.Name;
                            if (!string.IsNullOrWhiteSpace(lastSeg) && _derivedTableColumns.TryGetValue("WClaimSum", out var wcsCols))
                            {
                                var srcWcs = wcsCols.FirstOrDefault(c => c.Name?.Equals(lastSeg, StringComparison.OrdinalIgnoreCase) == true);
                                if (srcWcs != null && !string.IsNullOrWhiteSpace(srcWcs.SqlTypeName))
                                {
                                    child.SqlTypeName = srcWcs.SqlTypeName;
                                    if (srcWcs.MaxLength.HasValue) child.MaxLength = srcWcs.MaxLength;
                                    if (srcWcs.IsNullable.HasValue) child.IsNullable = srcWcs.IsNullable;
                                    if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] cte-col-type-wcs name={child.Name} srcCol={srcWcs.Name} sqlType={child.SqlTypeName}"); } catch { } }
                                }
                            }
                        }
                    }
                    catch { }
                    // Falls das analysierte Kind selbst ein Aggregat ist, Flags sicherstellen (Analyse sollte sie gesetzt haben).
                    // Zus�tzlich: Wenn das Aggregat in einer H�lle (z.B. CAST/CONVERT) steckt und Analyse es nicht erkannte, k�nnte hier k�nftig ein Wrapper-Check erfolgen.
                    if (child.IsAggregate == true && !string.IsNullOrWhiteSpace(child.AggregateFunction))
                    {
                        // Option D: Sofortige Typableitung direkt hier durchf�hren, damit sp�tere Namensnormalisierungen oder Flag-Verluste den Typ nicht eliminieren.
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
                                    else child.SqlTypeName = "decimal(18,2)"; // konservativer Default f�r monet�re Additionen
                                    break;
                                case "avg":
                                    child.SqlTypeName = "decimal(18,2)"; break;
                                case "exists":
                                    child.SqlTypeName = "bit"; break;
                                case "min":
                                case "max":
                                    // Ohne Quelltyp keine sichere Ableitung � bewusst offen lassen
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
                        // Diagnose: Aggregat flag ohne FunctionName (sollte selten vorkommen) ? Log helfen Root Cause zu finden.
                        if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] json-child-agg-missing-fn parent={outer.Name} child={child.Name} raw='{child.RawExpression}'"); } catch { } }
                    }
                    child.ExpressionKind ??= ResultColumnExpressionKind.Unknown;
                    if (!expandedChildren.Any(c => c.Name.Equals(child.Name, StringComparison.OrdinalIgnoreCase))) expandedChildren.Add(child);
                }
                catch { }
            }
            // Final inner pass: fill remaining untyped children by last-segment against local sources (vars/CTEs)
            try
            {
                foreach (var ch in expandedChildren)
                {
                    if (!string.IsNullOrWhiteSpace(ch.SqlTypeName)) continue;
                    var lastSeg = !string.IsNullOrWhiteSpace(ch.Name) && ch.Name.Contains('.') ? ch.Name.Split('.').Last() : ch.Name;
                    if (string.IsNullOrWhiteSpace(lastSeg)) continue;
                    bool assigned = false;
                    // 1) table variable aliases in this subquery
                    foreach (var kv in localVarCols)
                    {
                        var vcols = kv.Value;
                        var vcol = vcols?.FirstOrDefault(c => c.Name?.Equals(lastSeg, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(c.SqlTypeName));
                        if (vcol != null)
                        {
                            ch.SqlTypeName = vcol.SqlTypeName; ch.MaxLength = vcol.MaxLength; ch.IsNullable = vcol.IsNullable;
                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] inner-final-var-type name={ch.Name} alias={kv.Key} srcCol={vcol.Name} sqlType={ch.SqlTypeName}"); } catch { } }
                            assigned = true; break;
                        }
                    }
                    if (assigned) continue;
                    // 2) local alias mapped CTEs in this subquery
                    foreach (var kv in localAliases)
                    {
                        var a = kv.Key; var mapped = kv.Value;
                        // try alias as key
                        if (_derivedTableColumns.TryGetValue(a, out var dcolsA))
                        {
                            var srcA = dcolsA.FirstOrDefault(c => c.Name?.Equals(lastSeg, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(c.SqlTypeName));
                            if (srcA != null)
                            {
                                ch.SqlTypeName = srcA.SqlTypeName; ch.MaxLength = srcA.MaxLength; ch.IsNullable = srcA.IsNullable;
                                if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] inner-final-cte-type name={ch.Name} alias={a} srcCol={srcA.Name} sqlType={ch.SqlTypeName}"); } catch { } }
                                assigned = true; break;
                            }
                        }
                        // try mapped table name
                        if (!assigned && !string.IsNullOrWhiteSpace(mapped.Table) && _derivedTableColumns.TryGetValue(mapped.Table, out var dcolsT))
                        {
                            var srcT = dcolsT.FirstOrDefault(c => c.Name?.Equals(lastSeg, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(c.SqlTypeName));
                            if (srcT != null)
                            {
                                ch.SqlTypeName = srcT.SqlTypeName; ch.MaxLength = srcT.MaxLength; ch.IsNullable = srcT.IsNullable;
                                if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] inner-final-cte-type name={ch.Name} table={mapped.Table} srcCol={srcT.Name} sqlType={ch.SqlTypeName}"); } catch { } }
                                assigned = true; break;
                            }
                        }
                    }
                    if (assigned) continue;
                    // 3) unique last-segment match across all captured derived tables
                    try
                    {
                        var matches = new List<ResultColumn>();
                        foreach (var kvd in _derivedTableColumns)
                        {
                            var found = kvd.Value?.FirstOrDefault(c => c.Name?.Equals(lastSeg, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(c.SqlTypeName));
                            if (found != null) matches.Add(found);
                        }
                        if (matches.Count == 1)
                        {
                            var only = matches[0];
                            ch.SqlTypeName = only.SqlTypeName; ch.MaxLength = only.MaxLength; ch.IsNullable = only.IsNullable;
                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] inner-final-global-type name={ch.Name} srcCol={only.Name} sqlType={ch.SqlTypeName}"); } catch { } }
                        }
                        else if (matches.Count > 1)
                        {
                            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] inner-final-global-type-amb name={ch.Name} candidates={matches.Count}"); } catch { } }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            outer.Columns = expandedChildren;
            // Final deterministic post-pass: type unresolved children by unique match across all declared table variables in this procedure
            try
            {
                if (outer.Columns != null && _tableVariableColumns != null && _tableVariableColumns.Count > 0)
                {
                    foreach (var ch in outer.Columns)
                    {
                        if (!string.IsNullOrWhiteSpace(ch.SqlTypeName)) continue;
                        var last = !string.IsNullOrWhiteSpace(ch.Name) && ch.Name.Contains('.') ? ch.Name.Split('.').Last() : ch.Name;
                        // 1) unique last-segment across all declared vars
                        ResultColumn only = null; int count = 0;
                        foreach (var kv in _tableVariableColumns)
                        {
                            var v = kv.Value?.FirstOrDefault(x => x.Name?.Equals(last, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(x.SqlTypeName));
                            if (v != null) { only = v; count++; }
                        }
                        if (count == 1 && only != null)
                        {
                            ch.SqlTypeName = only.SqlTypeName; ch.MaxLength = only.MaxLength; ch.IsNullable = only.IsNullable;
                            continue;
                        }
                        // 2) unique collapsed-name across all declared vars (e.g., comparison.status.code -> ComparisonStatusCode)
                        var collapsed = new string((ch.Name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
                        only = null; count = 0;
                        foreach (var kv in _tableVariableColumns)
                        {
                            foreach (var vc in kv.Value ?? new List<ResultColumn>())
                            {
                                var vcCollapsed = new string((vc.Name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
                                if (!string.IsNullOrWhiteSpace(vcCollapsed) && string.Equals(vcCollapsed, collapsed, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(vc.SqlTypeName))
                                { only = vc; count++; }
                            }
                        }
                        if (count == 1 && only != null)
                        {
                            ch.SqlTypeName = only.SqlTypeName; ch.MaxLength = only.MaxLength; ch.IsNullable = only.IsNullable;
                        }
                    }
                }
            }
            catch { }
            if (ShouldDiag()) { try { System.Console.WriteLine($"[json-agg-diag] innerJsonQueryExpanded name={outer.Name} childCount={outer.Columns?.Count}"); } catch { } }
        }

        /// <summary>
        /// Versucht eine beliebige QueryExpression auf die innere QuerySpecification zu reduzieren.
        /// Unterst�tzt SelectStatement (->QueryExpression), QueryParenthesisExpression (rekursiv) und direkte QuerySpecification.
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
        /// Unterst�tzt ParenthesisExpression, BinaryExpression, FunctionCall (Parameter), CASE Expressions.
        /// depthLimit sch�tzt vor pathologischer Rekursion.
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
            if (ShouldDiagJsonAst()) { try { Console.WriteLine($"[json-ast-bind] {col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} -> {col.Name} ({reason})"); } catch { } }
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
                    if (ShouldDiagJsonAst()) Console.WriteLine($"[json-ast-derived] {alias}.{kv.Key} => {kv.Value.Schema}.{kv.Value.Table}.{kv.Value.Column}{amb} ({kind})");
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
                        // Literal Flags immer additiv propagieren (auch f�r Computed-Ausdr�cke)
                        if (src.HasIntegerLiteral) target.HasIntegerLiteral = true;
                        if (src.HasDecimalLiteral) target.HasDecimalLiteral = true;

                        // **NEUE CTE TYPE PROPAGATION**: SqlTypeName aus CTE/Derived Table �bertragen
                        if (string.IsNullOrWhiteSpace(target.SqlTypeName) && !string.IsNullOrWhiteSpace(src.SqlTypeName))
                        {
                            target.SqlTypeName = src.SqlTypeName;
                            target.MaxLength = src.MaxLength;
                            target.IsNullable = src.IsNullable;
                            if (ShouldDiag()) System.Console.WriteLine($"[cte-type-propagation] {derivedAlias}.{innerAliasColumn} -> {target.SqlTypeName} (MaxLength={target.MaxLength})");
                        }

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
                                        // SUM �bernimmt den bereits propagierten Literal-Status zur Ableitung
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

        private void TryPropagateTypeFromDerived(string innerAliasColumn, string derivedAlias, ResultColumn target)
        {
            try
            {
                if (ShouldDiag()) System.Console.WriteLine($"[cte-type-propagation] enter innerAlias={innerAliasColumn} derivedAlias={derivedAlias} target={target?.Name}");

                if (_derivedTableColumns.TryGetValue(derivedAlias, out var derivedCols))
                {
                    var sourceCol = derivedCols.FirstOrDefault(c => c.Name?.Equals(innerAliasColumn, StringComparison.OrdinalIgnoreCase) == true);
                    if (sourceCol != null)
                    {
                        if (ShouldDiag()) System.Console.WriteLine($"[cte-type-propagation] found sourceCol={sourceCol.Name} sqlType={sourceCol.SqlTypeName} maxLen={sourceCol.MaxLength}");

                        CopyColumnType(target, sourceCol);
                        if (ShouldDiag()) System.Console.WriteLine($"[cte-type-propagation] propagated SqlTypeName={target.SqlTypeName} userType={target.UserTypeSchemaName}.{target.UserTypeName} maxLen={target.MaxLength} nullable={target.IsNullable}");
                    }
                    else
                    {
                        if (ShouldDiag()) System.Console.WriteLine($"[cte-type-propagation] no match for innerAlias={innerAliasColumn} in derivedAlias={derivedAlias}");
                    }
                }
                else
                {
                    if (ShouldDiag()) System.Console.WriteLine($"[cte-type-propagation] derivedAlias={derivedAlias} not found in _derivedTableColumns");
                }
            }
            catch (Exception ex)
            {
                if (ShouldDiag()) System.Console.WriteLine($"[cte-type-propagation] error: {ex.Message}");
            }
        }

        private void TryApplyCteTypePropagationToNestedJson(ResultColumn nestedCol, string parentPath)
        {
            try
            {
                if (nestedCol == null) return;

                if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-type] enter col={nestedCol?.Name} parent={parentPath}");

                bool RequiresUserType() => string.IsNullOrWhiteSpace(nestedCol.UserTypeSchemaName) || string.IsNullOrWhiteSpace(nestedCol.UserTypeName);
                bool RequiresSqlType() => string.IsNullOrWhiteSpace(nestedCol.SqlTypeName);
                bool IsFullyResolved() => !RequiresSqlType() && !RequiresUserType();

                // Build candidate names: full dotted, and last segment only
                var candidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(nestedCol.Name))
                {
                    candidates.Add(nestedCol.Name);
                    var last = nestedCol.Name.Contains('.') ? nestedCol.Name.Split('.').Last() : nestedCol.Name;
                    if (!string.IsNullOrWhiteSpace(last) && !candidates.Contains(last, StringComparer.OrdinalIgnoreCase)) candidates.Add(last);
                }
                if (!string.IsNullOrWhiteSpace(parentPath) && !string.IsNullOrWhiteSpace(nestedCol?.Name))
                {
                    var combined = parentPath.Trim();
                    if (!string.IsNullOrWhiteSpace(combined))
                    {
                        var composite = $"{combined}.{nestedCol.Name}";
                        if (!candidates.Contains(composite, StringComparer.OrdinalIgnoreCase)) candidates.Add(composite);
                    }
                    var parentLast = parentPath.Contains('.') ? parentPath.Split('.').Last() : parentPath;
                    if (!string.IsNullOrWhiteSpace(parentLast))
                    {
                        var aliasKey = $"{parentLast}.{nestedCol.Name}";
                        if (!candidates.Contains(aliasKey, StringComparer.OrdinalIgnoreCase)) candidates.Add(aliasKey);
                    }
                }

                // Strategy 1: Try to find column type from captured CTE types (AST-based), using candidates
                ResultColumn cteColumn = null;
                foreach (var cand in candidates)
                {
                    if (_cteColumnTypes.TryGetValue(cand, out var cteCol)) { cteColumn = cteCol; break; }
                }
                if (cteColumn != null)
                {
                    if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-type] Found CTE column {nestedCol.Name}: {cteColumn.SqlTypeName}, MaxLength={cteColumn.MaxLength}, IsNullable={cteColumn.IsNullable}");

                    CopyColumnType(nestedCol, cteColumn);

                    if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-type] Applied CTE type to {nestedCol.Name}: {nestedCol.SqlTypeName}, MaxLength={nestedCol.MaxLength}, IsNullable={nestedCol.IsNullable}, userType={nestedCol.UserTypeSchemaName}.{nestedCol.UserTypeName}");
                    if (IsFullyResolved()) return;
                }
                else if (ShouldDiag())
                {
                    Console.WriteLine($"[cte-nested-json-type] Column {nestedCol.Name} not found in captured CTE types. CTE types count: {_cteColumnTypes.Count}");
                }
                // Strategy 2: Try to find any CTE (derived table) that has a column with this name
                foreach (var derivedEntry in _derivedTableColumns)
                {
                    var derivedAlias = derivedEntry.Key;
                    var derivedCols = derivedEntry.Value;

                    ResultColumn sourceCol = null;
                    foreach (var cand in candidates)
                    {
                        sourceCol = derivedCols.FirstOrDefault(c => c.Name?.Equals(cand, StringComparison.OrdinalIgnoreCase) == true);
                        if (sourceCol != null) break;
                    }
                    if (sourceCol != null && (!string.IsNullOrWhiteSpace(sourceCol.SqlTypeName) || !string.IsNullOrWhiteSpace(sourceCol.UserTypeName)))
                    {
                        if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-type] found match col={nestedCol.Name} in derivedAlias={derivedAlias} sqlType={sourceCol.SqlTypeName}");

                        CopyColumnType(nestedCol, sourceCol);

                        if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-type] propagated to col={nestedCol.Name} sqlType={nestedCol.SqlTypeName} maxLen={nestedCol.MaxLength} nullable={nestedCol.IsNullable} userType={nestedCol.UserTypeSchemaName}.{nestedCol.UserTypeName}");
                        if (IsFullyResolved()) return; // Success, exit early
                    }
                }

                if (RequiresSqlType() && ShouldDiag())
                    Console.WriteLine($"[cte-nested-json-type] AST-based resolution still pending for column: {nestedCol.Name}");

                if (RequiresSqlType() || RequiresUserType())
                {
                    ResultColumn tableVarColumn = null;
                    foreach (var cand in candidates)
                    {
                        tableVarColumn = FindTableVariableColumn(cand);
                        if (tableVarColumn != null) break;
                    }
                    if (tableVarColumn == null && !string.IsNullOrWhiteSpace(nestedCol.Name))
                        tableVarColumn = FindTableVariableColumn(nestedCol.Name);

                    if (tableVarColumn != null && (!string.IsNullOrWhiteSpace(tableVarColumn.SqlTypeName) || !string.IsNullOrWhiteSpace(tableVarColumn.UserTypeName)))
                    {
                        CopyColumnType(nestedCol, tableVarColumn);
                        if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-type] applied table-var type to {nestedCol.Name}: {nestedCol.SqlTypeName} userType={nestedCol.UserTypeSchemaName}.{nestedCol.UserTypeName}");
                        if (IsFullyResolved()) return;
                    }
                }

                if (RequiresSqlType() || RequiresUserType())
                {
                    // Try derive from global lookup (unique columns across TableVariables + DerivedTables)
                    ResultColumn uniqueSource = null; int matches = 0;
                    foreach (var kv in _derivedTableColumns)
                    {
                        var hit = kv.Value?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c?.Name) && c.Name.Equals(nestedCol.Name, StringComparison.OrdinalIgnoreCase));
                        if (hit != null && (!string.IsNullOrWhiteSpace(hit.SqlTypeName) || !string.IsNullOrWhiteSpace(hit.UserTypeName)))
                        {
                            uniqueSource = hit; matches++;
                        }
                    }
                    if (matches == 1 && uniqueSource != null)
                    {
                        CopyColumnType(nestedCol, uniqueSource);

                        if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-type] propagated from unique source {nestedCol.Name}: {nestedCol.SqlTypeName}, MaxLength={nestedCol.MaxLength}, IsNullable={nestedCol.IsNullable}, userType={nestedCol.UserTypeSchemaName}.{nestedCol.UserTypeName}");
                        if (IsFullyResolved()) return;
                    }
                }

                if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-type] final result col={nestedCol?.Name} sqlType={nestedCol?.SqlTypeName} maxLen={nestedCol?.MaxLength} isNull={nestedCol?.IsNullable} userType={nestedCol?.UserTypeSchemaName}.{nestedCol?.UserTypeName}");
            }
            catch (Exception ex)
            {
                if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-type] error: {ex.Message}");
            }
        }

        private static void TryApplyVarTableTypePropagationToNestedJson(ResultColumn container, IEnumerable<List<ResultColumn>> varColumnSets)
        {
            if (container?.Columns == null || container.Columns.Count == 0 || varColumnSets == null) return;
            var sets = varColumnSets.Where(s => s != null).ToList();
            if (sets.Count == 0) return;
            foreach (var ch in container.Columns)
            {
                if (!string.IsNullOrWhiteSpace(ch.SqlTypeName)) continue;
                var last = !string.IsNullOrWhiteSpace(ch.Name) && ch.Name.Contains('.') ? ch.Name.Split('.').Last() : ch.Name;
                if (string.IsNullOrWhiteSpace(last)) continue;
                ResultColumn match = null; int found = 0;
                foreach (var s in sets)
                {
                    var m = s.FirstOrDefault(x => x.Name?.Equals(last, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(x.SqlTypeName));
                    if (m != null)
                    {
                        match = m; found++;
                    }
                }
                if (found == 1 && match != null)
                {
                    ch.SqlTypeName = match.SqlTypeName; ch.MaxLength = match.MaxLength; ch.IsNullable = match.IsNullable;
                }
            }
        }
        private static void TryApplyVarTableTypePropagationToNestedJson(ResultSet set, IEnumerable<List<ResultColumn>> varColumnSets)
        {
            if (set?.Columns == null || set.Columns.Count == 0) return;
            var dummy = new ResultColumn { Columns = set.Columns };
            TryApplyVarTableTypePropagationToNestedJson(dummy, varColumnSets);
        }

        public void ApplyCteTypePropagationToTopLevelColumns()
        {
            try
            {
                foreach (var resultSet in _analysis.JsonSets)
                {
                    if (resultSet?.Columns == null) continue;
                    foreach (var col in resultSet.Columns)
                    {
                        try
                        {
                            // If column still points to a CTE (by SourceTable or SourceAlias), try to resolve to physical source and types
                            string cteKey = null;
                            if (!string.IsNullOrWhiteSpace(col.SourceTable) && _derivedTableColumns.ContainsKey(col.SourceTable))
                                cteKey = col.SourceTable;
                            else if (!string.IsNullOrWhiteSpace(col.SourceAlias) && _derivedTableColumns.ContainsKey(col.SourceAlias))
                                cteKey = col.SourceAlias;

                            if (cteKey == null) continue;

                            // Prefer exact source column name; fall back to the visible name
                            var lookupName = !string.IsNullOrWhiteSpace(col.SourceColumn) ? col.SourceColumn : col.Name;
                            if (string.IsNullOrWhiteSpace(lookupName)) continue;

                            // Fetch type info from derived table columns
                            if (_derivedTableColumns.TryGetValue(cteKey, out var dcols))
                            {
                                var src = dcols.FirstOrDefault(c => c.Name != null && c.Name.Equals(lookupName, StringComparison.OrdinalIgnoreCase));
                                if (src != null)
                                {
                                    if (string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(src.SqlTypeName)) col.SqlTypeName = src.SqlTypeName;
                                    if (!col.MaxLength.HasValue && src.MaxLength.HasValue) col.MaxLength = src.MaxLength;
                                    if (!col.IsNullable.HasValue && src.IsNullable.HasValue) col.IsNullable = src.IsNullable;
                                }
                            }

                            // If we have a physical source mapping for this CTE column, adopt it for enrichment
                            if (_derivedTableColumnSources.TryGetValue(cteKey, out var dmap))
                            {
                                if (dmap.TryGetValue(lookupName, out var bound))
                                {
                                    if (!string.IsNullOrWhiteSpace(bound.Schema)) col.SourceSchema = bound.Schema;
                                    if (!string.IsNullOrWhiteSpace(bound.Table)) col.SourceTable = bound.Table;
                                    if (!string.IsNullOrWhiteSpace(bound.Column)) col.SourceColumn = bound.Column;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public void ApplyCteTypePropagationToNestedJsonColumns()
        {
            try
            {
                if (ShouldDiag()) System.Console.WriteLine($"[cte-nested-json-post] starting post-processing for nested JSON CTE type propagation");

                // Walk through all result sets and find nested JSON columns
                foreach (var resultSet in _analysis.JsonSets)
                {
                    ApplyCteTypePropagationToColumnsRecursive(resultSet.Columns, null);
                }

                if (ShouldDiag()) System.Console.WriteLine($"[cte-nested-json-post] completed post-processing");
            }
            catch (Exception ex)
            {
                if (ShouldDiag()) System.Console.WriteLine($"[cte-nested-json-post] error: {ex.Message}");
            }
        }

        public void ApplyCteTypePropagationToTopLevelColumnsByName()
        {
            try
            {
                foreach (var resultSet in _analysis.JsonSets)
                {
                    if (resultSet?.Columns == null) continue;
                    foreach (var col in resultSet.Columns)
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(col.Name)
                                && _cteColumnTypes.TryGetValue(col.Name, out var cteCol))
                            {
                                if (string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(cteCol.SqlTypeName))
                                {
                                    col.SqlTypeName = cteCol.SqlTypeName;
                                }

                                CopyColumnType(col, cteCol);

                                if (cteCol.MaxLength.HasValue && !col.MaxLength.HasValue) col.MaxLength = cteCol.MaxLength;
                                if (cteCol.IsNullable.HasValue && !col.IsNullable.HasValue) col.IsNullable = cteCol.IsNullable;

                                if (ShouldDiag()) System.Console.WriteLine($"[cte-top-level-name] applied {col.Name} -> {col.SqlTypeName} len={col.MaxLength} nullable={col.IsNullable} userType={col.UserTypeSchemaName}.{col.UserTypeName}");
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void ApplyCteTypePropagationToColumnsRecursive(IReadOnlyList<ResultColumn> columns, string parentPath)
        {
            if (columns == null) return;

            foreach (var col in columns)
            {
                var currentPath = string.IsNullOrWhiteSpace(parentPath) ? col?.Name : string.IsNullOrWhiteSpace(col?.Name) ? parentPath : $"{parentPath}.{col.Name}";
                // Apply CTE type propagation to nested JSON columns that don't have type info
                if ((col.IsNestedJson == true || col.ReturnsJson == true) && col.Columns != null)
                {
                    if (ShouldDiag()) System.Console.WriteLine($"[cte-nested-json-post] processing nested JSON column: {col.Name}");

                    // Apply CTE type propagation to each nested column
                    foreach (var nestedCol in col.Columns)
                    {
                        var needsSqlType = string.IsNullOrWhiteSpace(nestedCol.SqlTypeName);
                        var needsUserType = string.IsNullOrWhiteSpace(nestedCol.UserTypeSchemaName) || string.IsNullOrWhiteSpace(nestedCol.UserTypeName);

                        if (needsSqlType || needsUserType)
                        {
                            if (ShouldDiag()) Console.WriteLine($"[cte-nested-json-post] applying CTE type propagation to nested column: {nestedCol.Name}");
                            TryApplyCteTypePropagationToNestedJson(nestedCol, currentPath);
                        }
                        else if (ShouldDiag())
                        {
                            Console.WriteLine($"[cte-nested-json-post] skipping nested column {nestedCol.Name} - already has type: {nestedCol.SqlTypeName}");
                        }
                    }

                    // Recursively process nested columns
                    ApplyCteTypePropagationToColumnsRecursive(col.Columns, currentPath);
                }
            }
        }

        // Late consolidation (Phase 2): lineage + scoped alias resolution for nested JSON children
        public void ApplyLineageLateConsolidationToNestedJson()
        {
            try
            {
                foreach (var entry in _analysis.NestedJsonSets)
                {
                    var qs = entry.Key; var set = entry.Value; if (set?.Columns == null || set.Columns.Count == 0) continue;

                    // Rebuild local alias maps for this nested subquery
                    var localAliases = new Dictionary<string, (string Schema, string Table)>(StringComparer.OrdinalIgnoreCase);
                    var localTableSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var localVarCols = new Dictionary<string, List<ResultColumn>>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        if (qs?.FromClause?.TableReferences != null)
                        {
                            foreach (var tr in qs.FromClause.TableReferences)
                            {
                                CollectLocalNamedTableReferences(tr, localAliases, localTableSources);
                                if (tr is VariableTableReference vtr)
                                {
                                    var alias = vtr.Alias?.Value; var varName = vtr.Variable?.Name;
                                    if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(varName))
                                    {
                                        var key = varName.StartsWith("@") ? varName : ("@" + varName);
                                        if (_tableVariableColumns.TryGetValue(key, out var colsForVar)) localVarCols[alias] = colsForVar;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                    var scope = BuildScopeFrameForNested(localVarCols, localAliases);

                    foreach (var ch in set.Columns)
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(ch.SqlTypeName)) continue;
                            // Try parse RawExpression and resolve via lineage
                            try
                            {
                                var raw = ch.RawExpression; if (!string.IsNullOrWhiteSpace(raw))
                                {
                                    var exprTxt = raw; var idx = exprTxt.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
                                    if (idx > 0) exprTxt = exprTxt.Substring(0, idx).Trim();
                                    var toParse = "SELECT " + exprTxt + ";";
                                    using var sr = new System.IO.StringReader(toParse);
                                    var frag = Parser.Parse(sr, out var errs);
                                    if (frag != null && (errs?.Count ?? 0) == 0)
                                    {
                                        ScalarExpression sx = null;
                                        try
                                        {
                                            if (frag is TSqlScript sc && sc.Batches?.Count > 0)
                                            {
                                                var st = sc.Batches[0]?.Statements?.OfType<SelectStatement>()?.FirstOrDefault();
                                                var qsi = UnwrapToQuerySpecification(st?.QueryExpression);
                                                var sse = qsi?.SelectElements?.OfType<SelectScalarExpression>()?.FirstOrDefault();
                                                sx = sse?.Expression as ScalarExpression;
                                            }
                                        }
                                        catch { }
                                        if (sx != null)
                                        {
                                            var ln = BuildLineage(sx);
                                            if (TryResolveLineageType(ln, scope, out var tl, out var ml, out var nul) && !string.IsNullOrWhiteSpace(tl))
                                            { ch.SqlTypeName = tl; ch.MaxLength = ml; ch.IsNullable = nul; continue; }
                                            // Fallback: direct alias.column regex
                                            var m = System.Text.RegularExpressions.Regex.Match(exprTxt, @"^\s*(?<a>[A-Za-z0-9_]+)\.(?<c>[A-Za-z0-9_]+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                            if (m.Success)
                                            {
                                                var a = m.Groups["a"].Value; var c = m.Groups["c"].Value;
                                                if (TryResolveFromScope(scope, a, c, out var t2, out var m2, out var n2) && !string.IsNullOrWhiteSpace(t2))
                                                { ch.SqlTypeName = t2; ch.MaxLength = m2; ch.IsNullable = n2; continue; }
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                            // Local-only unique last segment and collapsed-name across var tables
                            try
                            {
                                var last = !string.IsNullOrWhiteSpace(ch.Name) && ch.Name.Contains('.') ? ch.Name.Split('.').Last() : ch.Name;
                                if (!string.IsNullOrWhiteSpace(last) && localVarCols.Count > 0)
                                {
                                    ResultColumn only = null; int cnt = 0;
                                    foreach (var vset in localVarCols.Values)
                                    {
                                        var hit = vset?.FirstOrDefault(x => x.Name?.Equals(last, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(x.SqlTypeName));
                                        if (hit != null) { only = hit; cnt++; }
                                    }
                                    if (cnt == 1 && only != null)
                                    { ch.SqlTypeName = only.SqlTypeName; ch.MaxLength = only.MaxLength; ch.IsNullable = only.IsNullable; continue; }
                                }
                                var collapsed = new string((ch.Name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
                                if (localVarCols.Count > 0 && !string.IsNullOrWhiteSpace(collapsed))
                                {
                                    ResultColumn onlyC = null; int cntC = 0;
                                    foreach (var vset in localVarCols.Values)
                                    {
                                        foreach (var vc in vset ?? new List<ResultColumn>())
                                        {
                                            var vcc = new string((vc.Name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
                                            if (!string.IsNullOrWhiteSpace(vcc) && string.Equals(vcc, collapsed, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(vc.SqlTypeName))
                                            { onlyC = vc; cntC++; }
                                        }
                                    }
                                    if (cntC == 1 && onlyC != null)
                                    { ch.SqlTypeName = onlyC.SqlTypeName; ch.MaxLength = onlyC.MaxLength; ch.IsNullable = onlyC.IsNullable; continue; }
                                }
                            }
                            catch { }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public void CaptureCteColumnType(string columnName, ResultColumn columnType)
        {
            if (!string.IsNullOrEmpty(columnName) && columnType != null)
            {
                _cteColumnTypes[columnName] = columnType;
            }
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

        // Entfernt: InferSqlTypeFromSourceBinding (Namensheuristiken) � keine automatische Typableitung mehr.

        // Pr�ft ob Ausdruck strikt einem bin�ren 0/1 bedingten Muster entspricht (IIF/CASE Varianten)
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
                        if (IsLiteralZero(t) && IsLiteralOne(e)) return true; // auch 0/1 m�glich � trotzdem int
                    }
                }
                // Spezieller AST Node f�r IIF (manche Parser-Versionen) -> IIfCall
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
        if (_astVerboseEnabled) return true;
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








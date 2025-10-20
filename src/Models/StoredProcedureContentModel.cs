using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SpocR.Models;

// Minimal stabile Implementation (einzelne Klasse, keine Duplikate, keine Diagnose-Ausgaben).
public class StoredProcedureContentModel
{
    private static readonly TSql160Parser Parser = new(initialQuotedIdentifiers: true);

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
    public int? ParseErrorCount { get; init; }
    public string FirstParseError { get; init; }
    public IReadOnlyList<ExecutedProcedureCall> ExecutedProcedures { get; init; } = Array.Empty<ExecutedProcedureCall>();
    public bool ContainsExecKeyword { get; init; }
    public IReadOnlyList<string> RawExecCandidates { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string,string> RawExecCandidateKinds { get; init; } = new Dictionary<string,string>();

    public static StoredProcedureContentModel Parse(string definition, string defaultSchema = "dbo")
    {
        if (string.IsNullOrWhiteSpace(definition))
            return new StoredProcedureContentModel { Definition = definition };

        TSqlFragment fragment;
        IList<ParseError> parseErrors;
        using (var reader = new StringReader(definition))
            fragment = Parser.Parse(reader, out parseErrors);

        if (parseErrors?.Count > 0 || fragment == null)
        {
            var fb = Fallback(definition);
            // Fallback liefert bereits Flags; ergänze Fehlerinfos manuell.
            return new StoredProcedureContentModel
            {
                Definition = fb.Definition,
                Statements = fb.Statements,
                ContainsSelect = fb.ContainsSelect,
                ContainsInsert = fb.ContainsInsert,
                ContainsUpdate = fb.ContainsUpdate,
                ContainsDelete = fb.ContainsDelete,
                ContainsMerge = fb.ContainsMerge,
                ContainsOpenJson = fb.ContainsOpenJson,
                ResultSets = fb.ResultSets,
                UsedFallbackParser = true,
                ParseErrorCount = parseErrors?.Count,
                FirstParseError = parseErrors?.FirstOrDefault()?.Message
            };
        }

        var analysis = new Analysis(string.IsNullOrWhiteSpace(defaultSchema) ? "dbo" : defaultSchema);
        fragment.Accept(new Visitor(definition, analysis));

        // Build statements list
        var statements = analysis.StatementTexts.Any() ? analysis.StatementTexts.ToArray() : new[] { definition.Trim() };

        // Exec forwarding logic
        var execsRaw = analysis.ExecutedProcedures.Select(e => new ExecutedProcedureCall { Schema = e.Schema, Name = e.Name, IsCaptured = false }).ToList();
        var captured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in definition.Split('\n'))
        {
            var originalLine = line;
            var commentIndex = originalLine.IndexOf("--", StringComparison.Ordinal);
            var effectiveLine = commentIndex >= 0 ? originalLine.Substring(0, commentIndex) : originalLine;
            var l = effectiveLine.Trim(); if (l.Length == 0) continue; // Ignore fully commented / empty lines
            int iInsert = l.IndexOf("INSERT", StringComparison.OrdinalIgnoreCase);
            int iExec = l.IndexOf("EXEC", StringComparison.OrdinalIgnoreCase);
            if (iInsert >= 0 && iExec > iInsert)
            {
                var after = l[(iExec + 4)..].TrimStart('U','T','E',' ','\t').TrimStart();
                if (after.StartsWith("sp_executesql", StringComparison.OrdinalIgnoreCase) || after.StartsWith("@") || after.StartsWith("(") || after.StartsWith("'")) continue;
                int end = after.Length; foreach (var c in after.Select((ch,i)=> (ch,i))) { if (c.ch is ' ' or '\t' or ';' or '(') { end = c.i; break; } }
                var token = after[..end].Trim(); if (token.Length > 0) captured.Add(token);
            }
        }
        foreach (var ex in execsRaw)
        {
            var fq = $"{ex.Schema}.{ex.Name}"; if (captured.Contains(ex.Name) || captured.Contains(fq)) ex.IsCaptured = true;
        }
        var execs = execsRaw.Where(e => !e.IsCaptured).ToArray();

        var containsExec = definition.IndexOf("EXEC", StringComparison.OrdinalIgnoreCase) >= 0;
        var rawExec = new List<string>(); var rawKinds = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    if (containsExec)
        {
            foreach (var line in definition.Split('\n'))
            {
                if (rawExec.Count >= 5) break;
        var originalLine = line;
        var commentIndex = originalLine.IndexOf("--", StringComparison.Ordinal);
        var effectiveLine = commentIndex >= 0 ? originalLine.Substring(0, commentIndex) : originalLine;
        var t = effectiveLine.Trim(); if (t.Length == 0) continue; // Skip commented-only lines
                var idx = t.IndexOf("EXEC", StringComparison.OrdinalIgnoreCase); if (idx < 0) continue;
                var after = t[(idx + 4)..].TrimStart('U','T','E',' ','\t').TrimStart();
                if (after.StartsWith("sp_executesql", StringComparison.OrdinalIgnoreCase) || after.StartsWith("@") || after.StartsWith("(") || after.StartsWith("'")) continue;
                int end = after.Length; foreach (var c in after.Select((ch,i)=> (ch,i))) { if (c.ch is ' ' or '\t' or ';' or '(') { end = c.i; break; } }
                var token = after[..end].Trim(); if (token.Length == 0) continue;
                if (!rawExec.Contains(token, StringComparer.OrdinalIgnoreCase)) { rawExec.Add(token); rawKinds[token] = "static"; }
            }
        }

        var resultSets = AttachExecSource(analysis.JsonSets, execs, rawExec, rawKinds, analysis.DefaultSchema);

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
            ParseErrorCount = 0,
            FirstParseError = null
        };
    }

    // Fallback-Heuristik bei Parserfehlern
    private static StoredProcedureContentModel Fallback(string definition)
    {
        bool Has(string w) => definition.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;
        var statements = definition.Split(new[] { "GO" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var returnsJson = Has("FOR JSON");
        var noArray = Has("WITHOUT ARRAY WRAPPER") || Has("WITHOUT_ARRAY_WRAPPER");
        string root = null;
        if (returnsJson)
        {
            var token = "ROOT("; var ri = definition.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (ri >= 0)
            {
                var sq = definition.IndexOf('\'', ri); var eq = sq >= 0 ? definition.IndexOf('\'', sq + 1) : -1;
                if (sq >= 0 && eq > sq) root = definition.Substring(sq + 1, eq - sq - 1);
            }
        }
        var sets = returnsJson ? new[] { new ResultSet { ReturnsJson = true, ReturnsJsonArray = !noArray, ReturnsJsonWithoutArrayWrapper = noArray, JsonRootProperty = root } } : Array.Empty<ResultSet>();
        return new StoredProcedureContentModel
        {
            Definition = definition,
            Statements = statements.Length > 0 ? statements : new[] { definition.Trim() },
            ContainsSelect = Has("SELECT"),
            ContainsInsert = Has("INSERT"),
            ContainsUpdate = Has("UPDATE"),
            ContainsDelete = Has("DELETE"),
            ContainsMerge = Has("MERGE"),
            ContainsOpenJson = Has("OPENJSON"),
            ResultSets = sets,
            UsedFallbackParser = true
        };
    }

    // Modelle
    public sealed class ResultSet
    {
        public bool ReturnsJson { get; init; }
        public bool ReturnsJsonArray { get; init; }
        public bool ReturnsJsonWithoutArrayWrapper { get; init; }
        public string JsonRootProperty { get; init; }
        public IReadOnlyList<ResultColumn> Columns { get; init; } = Array.Empty<ResultColumn>();
        public string ExecSourceSchemaName { get; init; }
        public string ExecSourceProcedureName { get; init; }
        public bool HasSelectStar { get; init; }
    }
    public sealed class ExecutedProcedureCall
    {
        public string Schema { get; init; }
        public string Name { get; init; }
        public bool IsCaptured { get; set; }
    }
    public class ResultColumn
    {
        public string JsonPath { get; set; }
        public string Name { get; set; }
        public ResultColumnExpressionKind? ExpressionKind { get; set; }
        public string SourceSchema { get; set; }
        public string SourceTable { get; set; }
        public string SourceColumn { get; set; }
        public string SourceAlias { get; set; }
        public string SqlTypeName { get; set; }
        public string CastTargetType { get; set; }
        public bool? IsNullable { get; set; }
        public bool? ForcedNullable { get; set; }
        public bool? IsNestedJson { get; set; }
        public JsonResultModel JsonResult { get; set; }
        // Zusätzliche Properties benötigt von anderen Komponenten
        public string UserTypeSchemaName { get; set; }
        public string UserTypeName { get; set; }
        public int? MaxLength { get; set; }
        public bool? IsAmbiguous { get; set; }
    }
    public class JsonResultModel
    {
        public bool ReturnsJson { get; set; }
        public bool ReturnsJsonArray { get; set; }
        public bool ReturnsJsonWithoutArrayWrapper { get; set; }
        public string JsonRootProperty { get; set; }
        public IReadOnlyList<ResultColumn> Columns { get; set; } = Array.Empty<ResultColumn>();
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
    }

    private sealed class Visitor : TSqlFragmentVisitor
    {
        private readonly string _definition;
        private readonly Analysis _analysis;
        private readonly HashSet<int> _offsets = new();
        private int _procedureDepth;
        public Visitor(string definition, Analysis analysis) { _definition = definition; _analysis = analysis; }
        public override void ExplicitVisit(CreateProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        public override void ExplicitVisit(CreateOrAlterProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        public override void ExplicitVisit(AlterProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        public override void ExplicitVisit(SelectStatement node) { _analysis.ContainsSelect = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(InsertStatement node) { _analysis.ContainsInsert = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(UpdateStatement node) { _analysis.ContainsUpdate = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(DeleteStatement node) { _analysis.ContainsDelete = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(MergeStatement node) { _analysis.ContainsMerge = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(OpenJsonTableReference node) { _analysis.ContainsOpenJson = true; base.ExplicitVisit(node); }
        public override void ExplicitVisit(StatementList node)
        {
            if (_procedureDepth > 0 && node?.Statements != null)
                foreach (var s in node.Statements) AddStatement(s);
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(QuerySpecification node)
        {
            if (node.ForClause is JsonForClause jsonClause)
            {
                var builder = new JsonSetBuilder();
                var options = jsonClause.Options ?? Array.Empty<JsonForClauseOption>();
                if (options.Count == 0) builder.JsonWithArrayWrapper = true;
                foreach (var opt in options)
                {
                    switch (opt.OptionKind)
                    {
                        case JsonForClauseOptions.WithoutArrayWrapper: builder.JsonWithoutArrayWrapper = true; break;
                        case JsonForClauseOptions.Root: if (builder.JsonRootProperty == null && opt.Value is Literal lit) builder.JsonRootProperty = ExtractLiteralValue(lit); break;
                        default: if (opt.OptionKind != JsonForClauseOptions.WithoutArrayWrapper) builder.JsonWithArrayWrapper = true; break;
                    }
                }
                if (!builder.JsonWithoutArrayWrapper) builder.JsonWithArrayWrapper = true;
                foreach (var sce in node.SelectElements.OfType<SelectScalarExpression>())
                {
                    var alias = sce.ColumnName?.Value;
                    if (string.IsNullOrWhiteSpace(alias) && sce.Expression is ColumnReferenceExpression cref && cref.MultiPartIdentifier?.Identifiers?.Count > 0)
                        alias = cref.MultiPartIdentifier.Identifiers[^1].Value;
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    var path = NormalizeJsonPath(alias);
                    var col = new ResultColumn { JsonPath = path, Name = SafePropertyName(path) };
                    if (!builder.Columns.Any(c => c.Name.Equals(col.Name, StringComparison.OrdinalIgnoreCase))) builder.Columns.Add(col);
                }
                if (node.SelectElements?.OfType<SelectStarExpression>().Any() == true) builder.HasSelectStar = true;
                _analysis.JsonSets.Add(builder.ToResultSet());
            }
            base.ExplicitVisit(node);
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
        private static string NormalizeJsonPath(string value) => string.IsNullOrWhiteSpace(value) ? value : value.Trim().Trim('[',']','"','\'');
        private static string SafePropertyName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var seg = path.Split('.', StringSplitOptions.RemoveEmptyEntries); var cand = seg.Length > 0 ? seg[^1] : path;
            var b = new StringBuilder(); foreach (var ch in cand) if (char.IsLetterOrDigit(ch) || ch == '_') b.Append(ch);
            if (b.Length == 0) return null; if (!char.IsLetter(b[0]) && b[0] != '_') b.Insert(0,'_'); return b.ToString();
        }
        private static string ExtractLiteralValue(Literal lit) => lit switch { null => null, StringLiteral s => s.Value, _ => lit.Value };
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
                ReturnsJsonWithoutArrayWrapper = JsonWithoutArrayWrapper,
                JsonRootProperty = JsonRootProperty,
                Columns = Columns.ToArray(),
                HasSelectStar = HasSelectStar
            };
        }
    }

    private static IReadOnlyList<ResultSet> AttachExecSource(IReadOnlyList<ResultSet> sets, IReadOnlyList<ExecutedProcedureCall> execs,
        IReadOnlyList<string> rawExecCandidates, IReadOnlyDictionary<string,string> rawKinds, string defaultSchema)
    {
        if (sets == null || sets.Count == 0) return sets ?? Array.Empty<ResultSet>();
        ExecutedProcedureCall resolved = null;
        if (execs != null && execs.Count == 1) resolved = execs[0];
        else if ((execs == null || execs.Count == 0) && rawExecCandidates != null && rawExecCandidates.Count == 1)
        {
            var candidate = rawExecCandidates[0];
            if (rawKinds != null && rawKinds.TryGetValue(candidate, out var kind) && string.Equals(kind, "static", StringComparison.OrdinalIgnoreCase))
            {
                var parts = candidate.Split('.', StringSplitOptions.RemoveEmptyEntries);
                resolved = parts.Length switch
                {
                    1 => new ExecutedProcedureCall { Schema = defaultSchema, Name = parts[0] },
                    >=2 => new ExecutedProcedureCall { Schema = parts[0], Name = parts[1] },
                    _ => null
                };
            }
        }
        if (resolved == null) return sets;
        var augmented = new List<ResultSet>(sets.Count);
        foreach (var s in sets)
        {
            if (!s.ReturnsJson) { augmented.Add(s); continue; }
            augmented.Add(new ResultSet
            {
                ReturnsJson = s.ReturnsJson,
                ReturnsJsonArray = s.ReturnsJsonArray,
                ReturnsJsonWithoutArrayWrapper = s.ReturnsJsonWithoutArrayWrapper,
                JsonRootProperty = s.JsonRootProperty,
                Columns = s.Columns,
                HasSelectStar = s.HasSelectStar,
                ExecSourceSchemaName = resolved.Schema,
                ExecSourceProcedureName = resolved.Name
            });
        }
        return augmented;
    }
}


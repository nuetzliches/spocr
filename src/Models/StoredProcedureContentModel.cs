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
    // Global verbosity flag for AST binding diagnostics (bind / derived). Default false; enabled only when manager sets via --verbose.
    private static bool _astVerboseEnabled = false;
    public static void SetAstVerbose(bool enabled) => _astVerboseEnabled = enabled;

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
    public IReadOnlyDictionary<string, string> RawExecCandidateKinds { get; init; } = new Dictionary<string, string>();

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

        var containsExec = definition.IndexOf("EXEC", StringComparison.OrdinalIgnoreCase) >= 0;
        var rawExec = new List<string>(); var rawKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                var after = t[(idx + 4)..].TrimStart('U', 'T', 'E', ' ', '\t').TrimStart();
                if (after.StartsWith("sp_executesql", StringComparison.OrdinalIgnoreCase) || after.StartsWith("@") || after.StartsWith("(") || after.StartsWith("'")) continue;
                int end = after.Length; foreach (var c in after.Select((ch, i) => (ch, i))) { if (c.ch is ' ' or '\t' or ';' or '(') { end = c.i; break; } }
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
        // Nested JSON result sets produced by scalar subqueries (SELECT ... FOR JSON ...) inside a parent SELECT list.
        // Keyed by the inner QuerySpecification node so we can attach later when analyzing the parent scalar expression.
        public Dictionary<QuerySpecification, ResultSet> NestedJsonSets { get; } = new();
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
        public Visitor(string definition, Analysis analysis) { _definition = definition; _analysis = analysis; }
        public override void ExplicitVisit(CreateProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        public override void ExplicitVisit(CreateOrAlterProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        public override void ExplicitVisit(AlterProcedureStatement node) { _procedureDepth++; base.ExplicitVisit(node); _procedureDepth--; }
        private int _scalarSubqueryDepth; // Track nesting inside ScalarSubquery expressions
        public override void ExplicitVisit(SelectStatement node) { _analysis.ContainsSelect = true; base.ExplicitVisit(node); }
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
            // Scope isolation: reset alias/source tracking for each QuerySpecification to avoid bleed between statements / CTEs.
            _tableAliases.Clear();
            _tableSources.Clear();
            base.ExplicitVisit(node); // collect NamedTableReference + joins
            if (node.ForClause is not JsonForClause jsonClause) return;

            // Collect outer join right-side aliases BEFORE analyzing select elements (if not captured already)
            CollectOuterJoinRightAliases(node.FromClause?.TableReferences);

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
                if (string.IsNullOrWhiteSpace(alias))
                {
                    if (sce.Expression is ColumnReferenceExpression implicitCr && implicitCr.MultiPartIdentifier?.Identifiers?.Count > 0)
                        alias = implicitCr.MultiPartIdentifier.Identifiers[^1].Value;
                    else if (sce.Expression is CastCall castCall && castCall.Parameter is ColumnReferenceExpression castCol && castCol.MultiPartIdentifier?.Identifiers?.Count > 0)
                        alias = castCol.MultiPartIdentifier.Identifiers[^1].Value;
                }
                if (string.IsNullOrWhiteSpace(alias)) continue;
                var path = NormalizeJsonPath(alias);
                var col = new ResultColumn { JsonPath = path, Name = SafePropertyName(path) };
                var beforeBindings = new SourceBindingState();
                AnalyzeScalarExpression(sce.Expression, col, beforeBindings);
                col.ExpressionKind ??= ResultColumnExpressionKind.Unknown;
                // Outer join heuristic: if bound alias belongs to right-side of LEFT OUTER JOIN mark forced nullable
                if (!string.IsNullOrWhiteSpace(col.SourceAlias) && _outerJoinRightAliases.Contains(col.SourceAlias))
                    col.ForcedNullable = true;
                // Ambiguity: if multiple distinct source bindings encountered
                if (beforeBindings.BindingCount > 1) col.IsAmbiguous = true;
                if (!builder.Columns.Any(c => c.Name.Equals(col.Name, StringComparison.OrdinalIgnoreCase))) builder.Columns.Add(col);
            }
            if (node.SelectElements?.OfType<SelectStarExpression>().Any() == true) builder.HasSelectStar = true;
            var isNested = _scalarSubqueryDepth > 0; // Inside a scalar subquery context
            var resultSet = builder.ToResultSet();
            if (isNested)
            {
                // Store for later attachment to the parent column; do NOT add to top-level JsonSets.
                if (!_analysis.NestedJsonSets.ContainsKey(node))
                    _analysis.NestedJsonSets[node] = resultSet;
            }
            else
            {
                _analysis.JsonSets.Add(resultSet);
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
                                var columnMap = ExtractColumnSourceMapFromQuerySpecification(qs);
                                if (columnMap.Count > 0)
                                {
                                    _derivedTableColumnSources[alias] = columnMap;
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
            switch (expr)
            {
                case null:
                    return;
                case ColumnReferenceExpression cref:
                    target.ExpressionKind = ResultColumnExpressionKind.ColumnRef;
                    BindColumnReference(cref, target, state);
                    break;
                case CastCall castCall:
                    target.ExpressionKind = ResultColumnExpressionKind.Cast;
                    if (castCall.DataType?.Name?.Identifiers?.Count > 0)
                    {
                        var typeName = string.Join('.', castCall.DataType.Name.Identifiers.Select(i => i.Value));
                        if (!string.IsNullOrWhiteSpace(typeName)) target.CastTargetType = typeName;
                    }
                    AnalyzeScalarExpression(castCall.Parameter, target, state);
                    break;
                case ConvertCall convertCall:
                    target.ExpressionKind = ResultColumnExpressionKind.Cast;
                    if (convertCall.DataType?.Name?.Identifiers?.Count > 0)
                    {
                        var typeName = string.Join('.', convertCall.DataType.Name.Identifiers.Select(i => i.Value));
                        if (!string.IsNullOrWhiteSpace(typeName)) target.CastTargetType = typeName;
                    }
                    foreach (var p in new[] { convertCall.Parameter, convertCall.Style }) AnalyzeScalarExpression(p, target, state);
                    break;
                case FunctionCall fn:
                    // Distinguish JSON_QUERY
                    var fnName = fn.FunctionName?.Value;
                    if (!string.IsNullOrWhiteSpace(fnName) && fnName.Equals("JSON_QUERY", StringComparison.OrdinalIgnoreCase))
                        target.ExpressionKind = ResultColumnExpressionKind.JsonQuery;
                    else
                        target.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                    foreach (var p in fn.Parameters) AnalyzeScalarExpression(p, target, state);
                    break;
                // JSON_QUERY appears as FunctionCall with name 'JSON_QUERY'; we already classify via FunctionCall. No dedicated node.
                case BinaryExpression be:
                    // Computed
                    target.ExpressionKind = ResultColumnExpressionKind.Computed;
                    AnalyzeScalarExpression(be.FirstExpression, target, state);
                    AnalyzeScalarExpression(be.SecondExpression, target, state);
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
                        target.JsonResult = new JsonResultModel
                        {
                            ReturnsJson = nested.ReturnsJson,
                            ReturnsJsonArray = nested.ReturnsJsonArray,
                            ReturnsJsonWithoutArrayWrapper = nested.ReturnsJsonWithoutArrayWrapper,
                            JsonRootProperty = nested.JsonRootProperty,
                            Columns = nested.Columns
                        };
                        // We still traverse the inner query for potential source bindings? Already done via visitor; skip here.
                    }
                    // No further traversal needed; inner QuerySpecification already visited by the main visitor.
                    break;
                default:
                    // Unknown expression type -> attempt generic traversal via properties with ScalarExpression
                    break;
            }
        }
        private void BindColumnReference(ColumnReferenceExpression cref, ResultColumn col, SourceBindingState state)
        {
            if (cref?.MultiPartIdentifier?.Identifiers == null || cref.MultiPartIdentifier.Identifiers.Count == 0) return;
            var parts = cref.MultiPartIdentifier.Identifiers.Select(i => i.Value).ToList();
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
                }
                else if (_derivedTableColumnSources.TryGetValue(tableOrAlias, out var derivedMap) && derivedMap.TryGetValue(column, out var dsrc) && !string.IsNullOrWhiteSpace(dsrc.Schema))
                {
                    col.SourceAlias = tableOrAlias;
                    col.SourceSchema = dsrc.Schema;
                    col.SourceTable = dsrc.Table;
                    col.SourceColumn = dsrc.Column;
                    if (dsrc.Ambiguous) col.IsAmbiguous = true;
                    state.Register(col.SourceSchema, col.SourceTable, col.SourceColumn);
                    ConsoleWriteBind(col, reason: "alias-derived");
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
                ConsoleWriteBind(col, reason: "three-part");
            }
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

        // --- Derived table / CTE Verarbeitung ---
        private void ProcessQueryDerivedTable(QueryDerivedTable node)
        {
            var alias = node?.Alias?.Value; if (string.IsNullOrWhiteSpace(alias)) return;
            if (node.QueryExpression is not QuerySpecification qs) return; // Nur einfache SELECTs behandeln (kein UNION etc.)
            var columnMap = ExtractColumnSourceMapFromQuerySpecification(qs);
            if (columnMap.Count > 0)
            {
                _derivedTableColumnSources[alias] = columnMap;
                ConsoleWriteDerived(alias, columnMap, isCte: false);
            }
        }
        private Dictionary<string, (string Schema, string Table, string Column, bool Ambiguous)> ExtractColumnSourceMapFromQuerySpecification(QuerySpecification qs)
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
                if (string.IsNullOrWhiteSpace(alias))
                {
                    if (sce.Expression is ColumnReferenceExpression implicitCr && implicitCr.MultiPartIdentifier?.Identifiers?.Count > 0)
                        alias = implicitCr.MultiPartIdentifier.Identifiers[^1].Value;
                    else if (sce.Expression is CastCall castCall && castCall.Parameter is ColumnReferenceExpression castCol && castCol.MultiPartIdentifier?.Identifiers?.Count > 0)
                        alias = castCol.MultiPartIdentifier.Identifiers[^1].Value;
                }
                if (string.IsNullOrWhiteSpace(alias)) continue;
                var col = new ResultColumn();
                var state = new SourceBindingState();
                AnalyzeScalarExpressionDerived(sce.Expression, col, state, localAliases, localTableSources);
                var ambiguous = col.IsAmbiguous == true || state.BindingCount > 1;
                map[alias] = (col.SourceSchema, col.SourceTable, col.SourceColumn, ambiguous);
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
                    break;
                case CastCall castCall:
                    AnalyzeScalarExpressionDerived(castCall.Parameter, target, state, localAliases, localTableSources);
                    break;
                case ConvertCall convertCall:
                    AnalyzeScalarExpressionDerived(convertCall.Parameter, target, state, localAliases, localTableSources);
                    AnalyzeScalarExpressionDerived(convertCall.Style, target, state, localAliases, localTableSources);
                    break;
                case FunctionCall fn:
                    foreach (var p in fn.Parameters) AnalyzeScalarExpressionDerived(p, target, state, localAliases, localTableSources);
                    break;
                case BinaryExpression be:
                    AnalyzeScalarExpressionDerived(be.FirstExpression, target, state, localAliases, localTableSources);
                    AnalyzeScalarExpressionDerived(be.SecondExpression, target, state, localAliases, localTableSources);
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
            }
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
    }

    private static IReadOnlyList<ResultSet> AttachExecSource(IReadOnlyList<ResultSet> sets, IReadOnlyList<ExecutedProcedureCall> execs,
        IReadOnlyList<string> rawExecCandidates, IReadOnlyDictionary<string, string> rawKinds, string defaultSchema)
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
                    >= 2 => new ExecutedProcedureCall { Schema = parts[0], Name = parts[1] },
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


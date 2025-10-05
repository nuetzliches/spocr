using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SpocR.Models;

public class StoredProcedureContentModel
{
    private static readonly TSql160Parser Parser = new(initialQuotedIdentifiers: true);

    public string Definition { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string> Statements { get; init; } = Array.Empty<string>();

    public bool ContainsSelect { get; init; }
    public bool ContainsInsert { get; init; }
    public bool ContainsUpdate { get; init; }
    public bool ContainsDelete { get; init; }
    public bool ContainsMerge { get; init; }

    public bool ContainsOpenJson { get; init; }

    // Unified result set collection (formerly JsonResultSets)
    public IReadOnlyList<ResultSet> ResultSets { get; init; } = Array.Empty<ResultSet>();

    // Parse diagnostics
    public bool UsedFallbackParser { get; init; }
    public int? ParseErrorCount { get; init; }
    public string FirstParseError { get; init; }

    // Procedures executed directly via EXEC inside this procedure (schema + name)
    public IReadOnlyList<ExecutedProcedureCall> ExecutedProcedures { get; init; } = Array.Empty<ExecutedProcedureCall>();

    // Removed legacy mirrored columns; access via ResultSets[0].Columns if needed

    public static StoredProcedureContentModel Parse(string definition, string defaultSchema = "dbo")
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return new StoredProcedureContentModel
            {
                Definition = definition
            };
        }

        TSqlFragment fragment;
        IList<ParseError> parseErrors;
        using (var reader = new StringReader(definition))
        {
            fragment = Parser.Parse(reader, out parseErrors);
        }

        if (parseErrors?.Count > 0 || fragment == null)
        {
            var fallback = CreateFallbackModel(definition);
            return new StoredProcedureContentModel
            {
                Definition = fallback.Definition,
                Statements = fallback.Statements,
                ContainsSelect = fallback.ContainsSelect,
                ContainsInsert = fallback.ContainsInsert,
                ContainsUpdate = fallback.ContainsUpdate,
                ContainsDelete = fallback.ContainsDelete,
                ContainsMerge = fallback.ContainsMerge,
                ContainsOpenJson = fallback.ContainsOpenJson,
                ResultSets = fallback.ResultSets,
                UsedFallbackParser = true,
                ParseErrorCount = parseErrors?.Count,
                FirstParseError = parseErrors?.FirstOrDefault()?.Message
            };
        }

        var analysis = new ProcedureContentAnalysis(defaultSchema);
        var visitor = new ProcedureContentVisitor(definition, analysis);
        fragment.Accept(visitor);
        analysis.FinalizeJson();

        var statements = visitor.Statements.Any()
            ? visitor.Statements.ToArray()
            : new[] { definition.Trim() };

        var jsonSets = analysis.JsonSets.ToArray();
        var execs = analysis.ExecutedProcedures
            .Select(e => new ExecutedProcedureCall { Schema = e.Schema, Name = e.Name })
            .ToArray();

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
            ResultSets = jsonSets,
            ExecutedProcedures = execs,
            UsedFallbackParser = false,
            ParseErrorCount = 0,
            FirstParseError = null
        };
    }

    private static StoredProcedureContentModel CreateFallbackModel(string definition)
    {
        var statements = definition
            .Split(new[] { "GO" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();

        bool ContainsWord(string word) => definition.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        var returnsJson = ContainsWord("FOR JSON");
        // Detect both spaced and underscore variants (some tooling may normalize underscores)
        var returnsJsonWithoutArrayWrapper =
            definition.IndexOf("WITHOUT ARRAY WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0 ||
            definition.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;

        string root = null;
        if (returnsJson)
        {
            const string token = "ROOT(";
            var rootIndex = definition.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (rootIndex >= 0)
            {
                var startQuote = definition.IndexOf('\'', rootIndex);
                var endQuote = startQuote >= 0 ? definition.IndexOf('\'', startQuote + 1) : -1;
                if (startQuote >= 0 && endQuote > startQuote)
                {
                    root = definition.Substring(startQuote + 1, endQuote - startQuote - 1);
                }
            }
        }

        return new StoredProcedureContentModel
        {
            Definition = definition,
            Statements = statements.Length > 0 ? statements : new[] { definition.Trim() },
            ContainsSelect = ContainsWord("SELECT"),
            ContainsInsert = ContainsWord("INSERT"),
            ContainsUpdate = ContainsWord("UPDATE"),
            ContainsDelete = ContainsWord("DELETE"),
            ContainsMerge = ContainsWord("MERGE"),
            // legacy flags removed
            ContainsOpenJson = ContainsWord("OPENJSON"),
            ResultSets = returnsJson
                ? new[] { new ResultSet { ReturnsJson = returnsJson, ReturnsJsonArray = returnsJson && !returnsJsonWithoutArrayWrapper, ReturnsJsonWithoutArrayWrapper = returnsJsonWithoutArrayWrapper, JsonRootProperty = root, Columns = Array.Empty<ResultColumn>() } }
                : Array.Empty<ResultSet>(),
            UsedFallbackParser = true,
            ParseErrorCount = null,
            FirstParseError = null
        };
    }

    public class ResultColumn
    {
        public string JsonPath { get; set; }
        public string Name { get; set; }
        public string SourceSchema { get; set; }
        public string SourceTable { get; set; }
        public string SourceColumn { get; set; }
        // Added for unified non-JSON result typing (formerly from StoredProcedureOutputModel)
        public string SqlTypeName { get; set; }
        public bool? IsNullable { get; set; }
        // Preserve original length metadata (was present on legacy output model & inputs)
        public int? MaxLength { get; set; }
        // Advanced inference (parser v5)
        public string SourceAlias { get; set; }           // Table/CTE/UDTT alias origin when available
        public ResultColumnExpressionKind? ExpressionKind { get; set; } // Nature of the SELECT expression
        public bool? IsNestedJson { get; set; }           // JSON_QUERY or nested FOR JSON projection
        public bool? ForcedNullable { get; set; }         // True if nullability elevated due to OUTER join semantics
        public bool? IsAmbiguous { get; set; }            // True if multiple possible origins prevented concrete typing
        public string CastTargetType { get; set; }        // Raw CAST/CONVERT target type text (parser v5 heuristic)
        // UDTT reference (enrichment stage) when column semantically represents a structured Context or similar
        public string UserTypeSchemaName { get; set; }
        public string UserTypeName { get; set; }
        // Nested JSON result (scalar subquery FOR JSON) captured inline instead of separate ResultSet
        public JsonResultModel JsonResult { get; set; }
    }

    public class JsonResultModel
    {
        public bool ReturnsJson { get; set; }
        public bool ReturnsJsonArray { get; set; }
        public bool ReturnsJsonWithoutArrayWrapper { get; set; }
        public string JsonRootProperty { get; set; }
        public IReadOnlyList<ResultColumn> Columns { get; set; } = Array.Empty<ResultColumn>();
    }

    public enum ResultColumnExpressionKind
    {
        ColumnRef,
        Cast,
        FunctionCall,
        JsonQuery,
        Computed,
        Unknown
    }

    public sealed class ResultSet
    {
        public bool ReturnsJson { get; init; }
        public bool ReturnsJsonArray { get; init; }
        public bool ReturnsJsonWithoutArrayWrapper { get; init; }
        public string JsonRootProperty { get; init; }
        public IReadOnlyList<ResultColumn> Columns { get; init; } = Array.Empty<ResultColumn>();
        // Forwarding metadata: when this ResultSet originated from an executed procedure (wrapper cloning)
        public string ExecSourceSchemaName { get; init; }
        public string ExecSourceProcedureName { get; init; }
        // Indicates that the underlying FOR JSON SELECT contained one or more STAR projections (SELECT * or a.*)
        public bool HasSelectStar { get; init; }
    }

    public sealed class ExecutedProcedureCall
    {
        public string Schema { get; init; }
        public string Name { get; init; }
    }

    private sealed class ProcedureContentAnalysis
    {
        public ProcedureContentAnalysis(string defaultSchema)
        {
            DefaultSchema = string.IsNullOrWhiteSpace(defaultSchema) ? "dbo" : defaultSchema;
        }

        public string DefaultSchema { get; }
        public List<ResultColumn> JsonColumns { get; } = new(); // internal builder list retained
        public Dictionary<string, (string Schema, string Table)> AliasMap { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OuterJoinNullableAliases { get; } = new(StringComparer.OrdinalIgnoreCase); // aliases that come from an OUTER side
        public bool ContainsSelect { get; set; }
        public bool ContainsInsert { get; set; }
        public bool ContainsUpdate { get; set; }
        public bool ContainsDelete { get; set; }
        public bool ContainsMerge { get; set; }
        // legacy aggregated flags (first JSON set) kept for backward compatibility
        public bool ReturnsJson { get; set; }
        public bool ReturnsJsonArray { get; private set; }
        public bool ReturnsJsonWithoutArrayWrapper { get; private set; }
        public string JsonRootProperty { get; set; }
        public bool ContainsOpenJson { get; set; }
        public bool JsonWithArrayWrapper { get; set; }
        public bool JsonWithoutArrayWrapper { get; set; }
        public List<ResultSet> JsonSets { get; } = new();
        public List<(string Schema, string Name)> ExecutedProcedures { get; } = new();

        public void FinalizeJson()
        {
            if (ReturnsJson)
            {
                ReturnsJsonWithoutArrayWrapper = JsonWithoutArrayWrapper;
                ReturnsJsonArray = !JsonWithoutArrayWrapper;
            }
        }
    }

    private sealed class ProcedureContentVisitor : TSqlFragmentVisitor
    {
        private readonly string _definition;
        private readonly ProcedureContentAnalysis _analysis;
        private readonly List<string> _statements = new();
        private readonly HashSet<int> _statementOffsets = new();
        private int _procedureDepth;
        private int _scalarSubqueryDepth; // verschachtelte Subselects (SELECT ... FOR JSON PATH) als Skalar in äußerem SELECT
        // Track parent select element count to detect pass-through scalar subquery JSON (only one projection at outer level)
        private int _parentSelectElementCount = -1;

        public ProcedureContentVisitor(string definition, ProcedureContentAnalysis analysis)
        {
            _definition = definition;
            _analysis = analysis;
        }

        public IReadOnlyList<string> Statements => _statements;

        public override void ExplicitVisit(CreateProcedureStatement node)
        {
            _procedureDepth++;
            base.ExplicitVisit(node);
            _procedureDepth--;
        }

        public override void ExplicitVisit(CreateOrAlterProcedureStatement node)
        {
            _procedureDepth++;
            base.ExplicitVisit(node);
            _procedureDepth--;
        }

        public override void ExplicitVisit(AlterProcedureStatement node)
        {
            _procedureDepth++;
            base.ExplicitVisit(node);
            _procedureDepth--;
        }

        public override void ExplicitVisit(StatementList node)
        {
            if (_procedureDepth > 0 && node?.Statements != null)
            {
                foreach (var statement in node.Statements)
                {
                    AddStatement(statement);
                }
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(SelectStatement node)
        {
            _analysis.ContainsSelect = true;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(InsertStatement node)
        {
            _analysis.ContainsInsert = true;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(UpdateStatement node)
        {
            _analysis.ContainsUpdate = true;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(DeleteStatement node)
        {
            _analysis.ContainsDelete = true;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(MergeStatement node)
        {
            _analysis.ContainsMerge = true;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(OpenJsonTableReference node)
        {
            _analysis.ContainsOpenJson = true;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(QuerySpecification node)
        {
            var parentSelectCount = _parentSelectElementCount;
            // set current as new parent for nested QuerySpecifications
            _parentSelectElementCount = node.SelectElements?.Count ?? 0;
            if (node.ForClause is JsonForClause jsonClause)
            {
                // Wenn wir uns innerhalb eines ScalarSubquery befinden, dann handelt es sich um ein verschachteltes JSON Fragment
                // das als einzelnes NVARCHAR(MAX) Feld (Alias im äußeren Select) zurückgegeben wird. In diesem Fall KEIN eigenes ResultSet anlegen.
                // Ausnahme: Pass-through Fall (Eltern-Select hat genau 1 Projektion, die nur dieses ScalarSubquery enthält) => als top-level JSON behandeln
                if (_scalarSubqueryDepth > 0 && !(parentSelectCount == 1))
                {
                    // Markiere im äußersten aktuellen Set später die entsprechende Column als Nested (erfolgt in CollectJsonColumnsForSet über Expression-Typ Erkennung)
                    base.ExplicitVisit(node);
                    _parentSelectElementCount = parentSelectCount; // restore before return
                    return;
                }
                // Create per-query JSON set context
                var set = new JsonResultSetBuilder();
                set.ReturnsJson = true;

                var options = jsonClause.Options ?? Array.Empty<JsonForClauseOption>();
                if (options.Count == 0)
                {
                    set.JsonWithArrayWrapper = true;
                }

                foreach (var option in options)
                {
                    switch (option.OptionKind)
                    {
                        case JsonForClauseOptions.WithoutArrayWrapper:
                            set.JsonWithoutArrayWrapper = true;
                            break;
                        case JsonForClauseOptions.Root:
                            if (set.JsonRootProperty == null && option.Value is Literal literal)
                            {
                                set.JsonRootProperty = ExtractLiteralValue(literal);
                            }
                            break;
                        case JsonForClauseOptions.Path:
                        case JsonForClauseOptions.Auto:
                            set.JsonWithArrayWrapper = true;
                            break;
                        default:
                            if (option.OptionKind != JsonForClauseOptions.WithoutArrayWrapper)
                            {
                                set.JsonWithArrayWrapper = true;
                            }
                            break;
                    }
                }

                if (!set.JsonWithoutArrayWrapper)
                {
                    set.JsonWithArrayWrapper = true;
                }
                var collected = CollectJsonColumnsForSet(node);
                set.JsonColumns.AddRange(collected);
                // Detect star projections within this FOR JSON select
                if (node.SelectElements != null)
                {
                    foreach (var se in node.SelectElements)
                    {
                        if (se is SelectStarExpression)
                        {
                            set.HasSelectStar = true;
                            break;
                        }
                        // (Qualified wildcard like alias.* is also represented as SelectStarExpression in ScriptDom for SQL Server parser)
                    }
                }

                // finalize set flags
                set.Complete();
                _analysis.JsonSets.Add(set.ToResultSet());
                if (set.HasSelectStar)
                {
                    // Currently only flagged; expansion of STAR columns deferred to enrichment stage.
                }

                // Maintain legacy aggregated flags if first set
                // no legacy mirroring any more
            }

            base.ExplicitVisit(node);
            // restore parent context when unwinding
            _parentSelectElementCount = parentSelectCount;
        }

        public override void ExplicitVisit(ExecuteSpecification node)
        {
            try
            {
                if (node.ExecutableEntity is ExecutableProcedureReference epr)
                {
                    var procRef = epr.ProcedureReference?.ProcedureReference;
                    var name = procRef?.Name;
                    if (name != null)
                    {
                        string schemaName = null;
                        string procName = null;
                        var ids = name.Identifiers;
                        if (ids != null && ids.Count > 0)
                        {
                            // Support 1-part (Proc), 2-part (Schema.Proc), 3-part (Db.Schema.Proc)
                            if (ids.Count == 1)
                            {
                                procName = ids[^1].Value;
                                schemaName = _analysis.DefaultSchema;
                            }
                            else if (ids.Count >= 2)
                            {
                                procName = ids[^1].Value;
                                schemaName = ids[^2].Value; // second last is schema in 2- or 3-part name
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(procName))
                        {
                            _analysis.ExecutedProcedures.Add((schemaName ?? _analysis.DefaultSchema, procName));
                        }
                    }
                }
            }
            catch { /* best effort */ }
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(ScalarSubquery node)
        {
            _scalarSubqueryDepth++;
            base.ExplicitVisit(node);
            _scalarSubqueryDepth--;
        }

        private List<ResultColumn> CollectJsonColumnsForSet(QuerySpecification node)
        {
            var collected = new List<ResultColumn>();
            // Build / extend alias map & join metadata
            _analysis.AliasMap.Clear();
            _analysis.OuterJoinNullableAliases.Clear();
            if (node.FromClause?.TableReferences != null)
            {
                foreach (var tableReference in node.FromClause.TableReferences)
                {
                    CollectTableReference(tableReference, _analysis.AliasMap, parentJoin: null, isFirstSide: false);
                }
            }

            foreach (var element in node.SelectElements.OfType<SelectScalarExpression>())
            {
                var path = GetJsonPath(element);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }
                var expr = element.Expression;
                var jsonColumn = new ResultColumn { JsonPath = path, Name = GetSafePropertyName(path) };

                // Expression kind detection
                switch (expr)
                {
                    case ColumnReferenceExpression columnRef:
                        jsonColumn.ExpressionKind = ResultColumnExpressionKind.ColumnRef;
                        var identifiers = columnRef.MultiPartIdentifier?.Identifiers;
                        if (identifiers != null && identifiers.Count > 0)
                        {
                            jsonColumn.SourceColumn = identifiers[^1].Value;
                            if (identifiers.Count > 1)
                            {
                                var qualifier = identifiers[^2].Value;
                                jsonColumn.SourceAlias = qualifier;
                                if (_analysis.AliasMap.TryGetValue(qualifier, out var tableInfo))
                                {
                                    jsonColumn.SourceSchema = tableInfo.Schema;
                                    jsonColumn.SourceTable = tableInfo.Table;
                                    if (_analysis.OuterJoinNullableAliases.Contains(qualifier))
                                    {
                                        jsonColumn.ForcedNullable = true; // mark for later enrichment override
                                    }
                                }
                            }
                        }
                        break;
                    case FunctionCall fc:
                        var fname = fc.FunctionName?.Value;
                        if (!string.IsNullOrEmpty(fname) && fname.Equals("JSON_QUERY", StringComparison.OrdinalIgnoreCase))
                        {
                            jsonColumn.ExpressionKind = ResultColumnExpressionKind.JsonQuery;
                            jsonColumn.IsNestedJson = true;
                        }
                        else
                        {
                            jsonColumn.ExpressionKind = ResultColumnExpressionKind.FunctionCall;
                        }
                        break;
                    case CastCall castCall:
                        jsonColumn.ExpressionKind = ResultColumnExpressionKind.Cast;
                        jsonColumn.CastTargetType = RenderDataType(castCall.DataType);
                        break;
                    case ConvertCall convertCall:
                        jsonColumn.ExpressionKind = ResultColumnExpressionKind.Cast;
                        jsonColumn.CastTargetType = RenderDataType(convertCall.DataType);
                        break;
                    case ScalarSubquery subquery:
                        // Detect nested FOR JSON PATH inside scalar subquery to attach structured JsonResult
                        var nested = ExtractNestedJson(subquery);
                        if (nested != null)
                        {
                            jsonColumn.IsNestedJson = true;
                            jsonColumn.ExpressionKind = ResultColumnExpressionKind.JsonQuery; // treat similarly
                            jsonColumn.JsonResult = nested;
                            // Represent the outer scalar as nvarchar(max) container
                            if (string.IsNullOrEmpty(jsonColumn.SqlTypeName))
                            {
                                jsonColumn.SqlTypeName = "nvarchar(max)";
                            }
                        }
                        else
                        {
                            jsonColumn.ExpressionKind = ResultColumnExpressionKind.Computed;
                        }
                        break;
                    default:
                        jsonColumn.ExpressionKind = ResultColumnExpressionKind.Computed;
                        break;
                }

                if (string.IsNullOrEmpty(jsonColumn.Name))
                {
                    continue;
                }

                if (!collected.Any(c => string.Equals(c.Name, jsonColumn.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    collected.Add(jsonColumn);
                }
            }
            return collected;
        }

        private static JsonResultModel ExtractNestedJson(ScalarSubquery subquery)
        {
            if (subquery?.QueryExpression is QuerySpecification qs && qs.ForClause is JsonForClause jsonClause)
            {
                var nestedCols = new List<ResultColumn>();
                // Collect nested select elements similar to CollectJsonColumnsForSet but simplified (no table provenance mapping here)
                foreach (var element in qs.SelectElements.OfType<SelectScalarExpression>())
                {
                    var alias = element.ColumnName?.Value;
                    if (string.IsNullOrWhiteSpace(alias) && element.Expression is ColumnReferenceExpression cref && cref.MultiPartIdentifier?.Identifiers?.Count > 0)
                    {
                        alias = cref.MultiPartIdentifier.Identifiers[^1].Value;
                    }
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    var path = NormalizeJsonPath(alias);
                    var col = new ResultColumn
                    {
                        JsonPath = path,
                        Name = GetSafePropertyName(path)
                    };
                    if (!nestedCols.Any(c => c.Name.Equals(col.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        nestedCols.Add(col);
                    }
                }
                bool arrayWrapper = true; bool withoutArray = false; string root = null;
                var options = jsonClause.Options ?? Array.Empty<JsonForClauseOption>();
                if (options.Count == 0) arrayWrapper = true;
                foreach (var option in options)
                {
                    switch (option.OptionKind)
                    {
                        case JsonForClauseOptions.WithoutArrayWrapper:
                            withoutArray = true; arrayWrapper = false; break;
                        case JsonForClauseOptions.Root:
                            if (option.Value is Literal lit) root = ExtractLiteralValue(lit); break;
                        default:
                            arrayWrapper = true; break;
                    }
                }
                if (!withoutArray) arrayWrapper = true;
                return new JsonResultModel
                {
                    ReturnsJson = true,
                    ReturnsJsonArray = arrayWrapper && !withoutArray,
                    ReturnsJsonWithoutArrayWrapper = withoutArray,
                    JsonRootProperty = root,
                    Columns = nestedCols.ToArray()
                };
            }
            return null;
        }

        // Builder to accumulate per JSON result set
        private sealed class JsonResultSetBuilder
        {
            public bool ReturnsJson { get; set; }
            public bool JsonWithArrayWrapper { get; set; }
            public bool JsonWithoutArrayWrapper { get; set; }
            public string JsonRootProperty { get; set; }
            public List<ResultColumn> JsonColumns { get; } = new();
            public bool HasSelectStar { get; set; }

            public void Complete()
            {
                // nothing additional yet
            }

            public ResultSet ToResultSet() => new()
            {
                ReturnsJson = ReturnsJson,
                ReturnsJsonArray = JsonWithArrayWrapper && !JsonWithoutArrayWrapper,
                ReturnsJsonWithoutArrayWrapper = JsonWithoutArrayWrapper,
                JsonRootProperty = JsonRootProperty,
                Columns = JsonColumns.ToArray(),
                HasSelectStar = HasSelectStar
            };
        }

        private static string GetJsonPath(SelectScalarExpression element)
        {
            var alias = element.ColumnName?.Value;
            if (!string.IsNullOrEmpty(alias))
            {
                return NormalizeJsonPath(alias);
            }

            if (element.Expression is ColumnReferenceExpression columnRef)
            {
                var identifiers = columnRef.MultiPartIdentifier?.Identifiers;
                if (identifiers != null && identifiers.Count > 0)
                {
                    return NormalizeJsonPath(identifiers[^1].Value);
                }
            }

            return null;
        }

        private static string NormalizeJsonPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var trimmed = value.Trim().Trim('[', ']', (char)34, (char)39);
            return trimmed;
        }

        private static string GetSafePropertyName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var candidate = segments.Length > 0 ? segments[^1] : path;

            var builder = new StringBuilder();
            foreach (var ch in candidate)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_')
                {
                    builder.Append(ch);
                }
            }

            if (builder.Length == 0)
            {
                return null;
            }

            if (!char.IsLetter(builder[0]) && builder[0] != '_')
            {
                builder.Insert(0, '_');
            }

            return builder.ToString();
        }

        private void CollectTableReference(TableReference tableReference, Dictionary<string, (string Schema, string Table)> aliasMap, QualifiedJoinType? parentJoin, bool isFirstSide)
        {
            switch (tableReference)
            {
                case NamedTableReference named:
                    var schema = named.SchemaObject?.SchemaIdentifier?.Value ?? _analysis.DefaultSchema;
                    var table = named.SchemaObject?.BaseIdentifier?.Value;
                    var alias = named.Alias?.Value ?? table;
                    if (!string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(table))
                    {
                        aliasMap[alias] = (schema, table);
                        // Mark nullable if this table is on the outer side of a LEFT/RIGHT/FULL join.
                        if (parentJoin.HasValue)
                        {
                            switch (parentJoin.Value)
                            {
                                case QualifiedJoinType.LeftOuter:
                                    if (!isFirstSide) _analysis.OuterJoinNullableAliases.Add(alias); // second side
                                    break;
                                case QualifiedJoinType.RightOuter:
                                    if (isFirstSide) _analysis.OuterJoinNullableAliases.Add(alias); // first side is right side's nullable side
                                    break;
                                case QualifiedJoinType.FullOuter:
                                    _analysis.OuterJoinNullableAliases.Add(alias);
                                    break;
                            }
                        }
                    }
                    break;
                case QualifiedJoin join:
                    // Recurse with join type context
                    CollectTableReference(join.FirstTableReference, aliasMap, join.QualifiedJoinType, true);
                    CollectTableReference(join.SecondTableReference, aliasMap, join.QualifiedJoinType, false);
                    break;
                case JoinParenthesisTableReference parenthesis when parenthesis.Join != null:
                    CollectTableReference(parenthesis.Join, aliasMap, parentJoin, isFirstSide);
                    break;
            }
        }
        private void AddStatement(TSqlStatement statement)
        {
            if (statement?.StartOffset >= 0 && statement.FragmentLength > 0)
            {
                var end = Math.Min(_definition.Length, statement.StartOffset + statement.FragmentLength);
                if (_statementOffsets.Add(statement.StartOffset))
                {
                    var text = _definition.Substring(statement.StartOffset, end - statement.StartOffset).Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        _statements.Add(text);
                    }
                }
            }
        }

        private static string RenderDataType(DataTypeReference dataType)
        {
            if (dataType == null) return null;
            switch (dataType)
            {
                case SqlDataTypeReference sqlRef:
                    var baseName = sqlRef.SqlDataTypeOption.ToString();
                    string Map(string v) => v switch
                    {
                        nameof(SqlDataTypeOption.NVarChar) => "nvarchar",
                        nameof(SqlDataTypeOption.VarChar) => "varchar",
                        nameof(SqlDataTypeOption.VarBinary) => "varbinary",
                        nameof(SqlDataTypeOption.NChar) => "nchar",
                        nameof(SqlDataTypeOption.Char) => "char",
                        nameof(SqlDataTypeOption.NText) => "ntext",
                        nameof(SqlDataTypeOption.Text) => "text",
                        nameof(SqlDataTypeOption.Int) => "int",
                        nameof(SqlDataTypeOption.BigInt) => "bigint",
                        nameof(SqlDataTypeOption.SmallInt) => "smallint",
                        nameof(SqlDataTypeOption.TinyInt) => "tinyint",
                        nameof(SqlDataTypeOption.Bit) => "bit",
                        nameof(SqlDataTypeOption.DateTime) => "datetime",
                        nameof(SqlDataTypeOption.Date) => "date",
                        nameof(SqlDataTypeOption.UniqueIdentifier) => "uniqueidentifier",
                        nameof(SqlDataTypeOption.Decimal) => "decimal",
                        nameof(SqlDataTypeOption.Numeric) => "numeric",
                        nameof(SqlDataTypeOption.Money) => "money",
                        _ => baseName.ToLowerInvariant()
                    };
                    var typeName = Map(baseName);
                    if (sqlRef.Parameters != null && sqlRef.Parameters.Count > 0)
                    {
                        var parts = new List<string>();
                        foreach (var p in sqlRef.Parameters)
                        {
                            if (p is MaxLiteral) { parts.Add("max"); }
                            else if (p is IntegerLiteral il) { parts.Add(il.Value); }
                            else if (p is Literal l && !string.IsNullOrWhiteSpace(l.Value)) { parts.Add(l.Value); }
                        }
                        if (parts.Count > 0)
                        {
                            typeName += "(" + string.Join(",", parts) + ")";
                        }
                    }
                    return typeName;
                case UserDataTypeReference userRef:
                    return userRef.Name?.BaseIdentifier?.Value;
                default:
                    return null;
            }
        }

        private static string ExtractLiteralValue(Literal literal) => literal switch
        {
            null => null,
            StringLiteral stringLiteral => stringLiteral.Value,
            _ => literal.Value
        };
    }
}


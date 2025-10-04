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
    }

    public sealed class ResultSet
    {
        public bool ReturnsJson { get; init; }
        public bool ReturnsJsonArray { get; init; }
        public bool ReturnsJsonWithoutArrayWrapper { get; init; }
        public string JsonRootProperty { get; init; }
        public IReadOnlyList<ResultColumn> Columns { get; init; } = Array.Empty<ResultColumn>();
    }

    private sealed class ProcedureContentAnalysis
    {
        public ProcedureContentAnalysis(string defaultSchema)
        {
            DefaultSchema = string.IsNullOrWhiteSpace(defaultSchema) ? "dbo" : defaultSchema;
        }

        public string DefaultSchema { get; }
        public List<ResultColumn> JsonColumns { get; } = new(); // internal builder list retained
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
            if (node.ForClause is JsonForClause jsonClause)
            {
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

                // finalize set flags
                set.Complete();
                _analysis.JsonSets.Add(set.ToResultSet());

                // Maintain legacy aggregated flags if first set
                // no legacy mirroring any more
            }

            base.ExplicitVisit(node);
        }

        private List<ResultColumn> CollectJsonColumnsForSet(QuerySpecification node)
        {
            var collected = new List<ResultColumn>();
            var aliasMap = new Dictionary<string, (string Schema, string Table)>(StringComparer.OrdinalIgnoreCase);
            if (node.FromClause?.TableReferences != null)
            {
                foreach (var tableReference in node.FromClause.TableReferences)
                {
                    CollectTableReference(tableReference, aliasMap);
                }
            }

            foreach (var element in node.SelectElements.OfType<SelectScalarExpression>())
            {
                var path = GetJsonPath(element);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var jsonColumn = new ResultColumn
                {
                    JsonPath = path,
                    Name = GetSafePropertyName(path)
                };

                if (element.Expression is ColumnReferenceExpression columnRef)
                {
                    var identifiers = columnRef.MultiPartIdentifier?.Identifiers;
                    if (identifiers != null && identifiers.Count > 0)
                    {
                        jsonColumn.SourceColumn = identifiers[^1].Value;
                        if (identifiers.Count > 1)
                        {
                            var qualifier = identifiers[^2].Value;
                            if (aliasMap.TryGetValue(qualifier, out var tableInfo))
                            {
                                jsonColumn.SourceSchema = tableInfo.Schema;
                                jsonColumn.SourceTable = tableInfo.Table;
                            }
                        }
                    }
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

        // Builder to accumulate per JSON result set
        private sealed class JsonResultSetBuilder
        {
            public bool ReturnsJson { get; set; }
            public bool JsonWithArrayWrapper { get; set; }
            public bool JsonWithoutArrayWrapper { get; set; }
            public string JsonRootProperty { get; set; }
            public List<ResultColumn> JsonColumns { get; } = new();

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
                Columns = JsonColumns.ToArray()
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

        private void CollectTableReference(TableReference tableReference, Dictionary<string, (string Schema, string Table)> aliasMap)
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
                    }
                    break;
                case QualifiedJoin join:
                    CollectTableReference(join.FirstTableReference, aliasMap);
                    CollectTableReference(join.SecondTableReference, aliasMap);
                    break;
                case JoinParenthesisTableReference parenthesis when parenthesis.Join != null:
                    CollectTableReference(parenthesis.Join, aliasMap);
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

        private static string ExtractLiteralValue(Literal literal) => literal switch
        {
            null => null,
            StringLiteral stringLiteral => stringLiteral.Value,
            _ => literal.Value
        };
    }
}


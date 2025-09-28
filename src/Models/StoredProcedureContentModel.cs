using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
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

    public bool ReturnsJson { get; init; }
    public bool ReturnsJsonArray { get; init; }
    public bool ReturnsJsonWithoutArrayWrapper { get; init; }
    public string JsonRootProperty { get; init; }
    public bool ContainsOpenJson { get; init; }

    public static StoredProcedureContentModel Parse(string definition)
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
            return CreateFallbackModel(definition);
        }

        var analysis = new ProcedureContentAnalysis();
        var visitor = new ProcedureContentVisitor(definition, analysis);
        fragment.Accept(visitor);
        analysis.FinalizeJson();

        var statements = visitor.Statements.Any()
            ? visitor.Statements.ToArray()
            : new[] { definition.Trim() };

        return new StoredProcedureContentModel
        {
            Definition = definition,
            Statements = statements,
            ContainsSelect = analysis.ContainsSelect,
            ContainsInsert = analysis.ContainsInsert,
            ContainsUpdate = analysis.ContainsUpdate,
            ContainsDelete = analysis.ContainsDelete,
            ContainsMerge = analysis.ContainsMerge,
            ReturnsJson = analysis.ReturnsJson,
            ReturnsJsonArray = analysis.ReturnsJsonArray,
            ReturnsJsonWithoutArrayWrapper = analysis.ReturnsJsonWithoutArrayWrapper,
            JsonRootProperty = analysis.JsonRootProperty,
            ContainsOpenJson = analysis.ContainsOpenJson
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
        var returnsJsonWithoutArrayWrapper = definition.IndexOf("WITHOUT ARRAY WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;

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
            ReturnsJson = returnsJson,
            ReturnsJsonArray = returnsJson && !returnsJsonWithoutArrayWrapper,
            ReturnsJsonWithoutArrayWrapper = returnsJsonWithoutArrayWrapper,
            JsonRootProperty = root,
            ContainsOpenJson = ContainsWord("OPENJSON")
        };
    }

    private sealed class ProcedureContentAnalysis
    {
        public bool ContainsSelect { get; set; }
        public bool ContainsInsert { get; set; }
        public bool ContainsUpdate { get; set; }
        public bool ContainsDelete { get; set; }
        public bool ContainsMerge { get; set; }
        public bool ReturnsJson { get; set; }
        public bool ReturnsJsonArray { get; private set; }
        public bool ReturnsJsonWithoutArrayWrapper { get; private set; }
        public string JsonRootProperty { get; set; }
        public bool ContainsOpenJson { get; set; }
        public bool JsonWithArrayWrapper { get; set; }
        public bool JsonWithoutArrayWrapper { get; set; }

        public void FinalizeJson()
        {
            ReturnsJsonArray = ReturnsJson && JsonWithArrayWrapper && !JsonWithoutArrayWrapper;
            ReturnsJsonWithoutArrayWrapper = ReturnsJson && JsonWithoutArrayWrapper;
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
                _analysis.ReturnsJson = true;

                var options = jsonClause.Options ?? Array.Empty<JsonForClauseOption>();
                if (options.Count == 0)
                {
                    _analysis.JsonWithArrayWrapper = true;
                }

                foreach (var option in options)
                {
                    switch (option.OptionKind)
                    {
                        case JsonForClauseOptions.WithoutArrayWrapper:
                            _analysis.JsonWithoutArrayWrapper = true;
                            break;
                        case JsonForClauseOptions.Root:
                            if (_analysis.JsonRootProperty == null && option.Value is Literal literal)
                            {
                                _analysis.JsonRootProperty = ExtractLiteralValue(literal);
                            }
                            break;
                        case JsonForClauseOptions.Path:
                        case JsonForClauseOptions.Auto:
                            _analysis.JsonWithArrayWrapper = true;
                            break;
                        default:
                            if (option.OptionKind != JsonForClauseOptions.WithoutArrayWrapper)
                            {
                                _analysis.JsonWithArrayWrapper = true;
                            }
                            break;
                    }
                }

                if (!_analysis.JsonWithoutArrayWrapper)
                {
                    _analysis.JsonWithArrayWrapper = true;
                }
            }

            base.ExplicitVisit(node);
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


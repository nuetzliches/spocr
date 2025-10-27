using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Augments a <see cref="ProcedureModel"/> by re-analyzing the SQL definition with ScriptDom to capture aggregate metadata.
/// </summary>
internal static class ProcedureModelAggregateAnalyzer
{
    private sealed record AggregateInfo(bool IsAggregate, string? FunctionName, bool HasIntegerLiteral, bool HasDecimalLiteral);

    public static void Apply(string? definition, ProcedureModel? model)
    {
        var fragment = ProcedureModelScriptDomParser.Parse(definition);
        Apply(fragment, model);
    }

    public static void Apply(TSqlFragment? fragment, ProcedureModel? model)
    {
        if (fragment == null || model == null)
        {
            return;
        }

        var visitor = new AggregateVisitor();
        fragment.Accept(visitor);

        if (visitor.Columns.Count == 0)
        {
            return;
        }

        foreach (var (alias, info) in visitor.Columns)
        {
            if (!info.IsAggregate)
            {
                continue;
            }

            var column = FindColumn(model, alias);
            if (column == null)
            {
                continue;
            }

            column.IsAggregate = true;
            if (!string.IsNullOrWhiteSpace(info.FunctionName))
            {
                column.AggregateFunction = info.FunctionName;
            }

            if (info.HasIntegerLiteral)
            {
                column.HasIntegerLiteral = true;
            }
            if (info.HasDecimalLiteral)
            {
                column.HasDecimalLiteral = true;
            }
        }
    }

    private static ProcedureResultColumn? FindColumn(ProcedureModel model, string alias)
    {
        if (model.ResultSets == null)
        {
            return null;
        }

        foreach (var resultSet in model.ResultSets)
        {
            var column = FindColumn(resultSet?.Columns, alias);
            if (column != null)
            {
                return column;
            }
        }

        return null;
    }

    private static ProcedureResultColumn? FindColumn(IReadOnlyList<ProcedureResultColumn>? columns, string alias)
    {
        if (columns == null)
        {
            return null;
        }

        foreach (var column in columns)
        {
            if (column == null)
            {
                continue;
            }

            if (IsAliasMatch(column.Name, alias))
            {
                return column;
            }

            var nested = FindColumn(column.Columns, alias);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool IsAliasMatch(string? candidate, string alias)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalizedCandidate = UnwrapIdentifier(candidate);
        var normalizedAlias = UnwrapIdentifier(alias);
        return string.Equals(normalizedCandidate, normalizedAlias, StringComparison.OrdinalIgnoreCase);
    }

    private static string UnwrapIdentifier(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private sealed class AggregateVisitor : TSqlFragmentVisitor
    {
        private static readonly HashSet<string> AggregateNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "count",
            "count_big",
            "sum",
            "avg",
            "min",
            "max",
            "exists"
        };

        public Dictionary<string, AggregateInfo> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);

        private int _queryDepth;
        private int _scalarSubqueryDepth;

        public override void ExplicitVisit(QuerySpecification node)
        {
            _queryDepth++;
            base.ExplicitVisit(node);
            _queryDepth--;
        }

        public override void ExplicitVisit(ScalarSubquery node)
        {
            _scalarSubqueryDepth++;
            base.ExplicitVisit(node);
            _scalarSubqueryDepth--;
        }

        public override void ExplicitVisit(SelectScalarExpression node)
        {
            if (_queryDepth != 1 || _scalarSubqueryDepth > 0)
            {
                base.ExplicitVisit(node);
                return;
            }

            var alias = node.ColumnName?.Value;
            if (string.IsNullOrWhiteSpace(alias))
            {
                base.ExplicitVisit(node);
                return;
            }

            var info = Analyze(node.Expression);
            if (info.IsAggregate)
            {
                Columns[alias] = info;
            }

            base.ExplicitVisit(node);
        }

        private AggregateInfo Analyze(ScalarExpression expression)
        {
            if (expression == null)
            {
                return new AggregateInfo(false, null, false, false);
            }

            return expression switch
            {
                IntegerLiteral => new AggregateInfo(false, null, true, false),
                NumericLiteral numeric => numeric.Value?.Contains('.') == true
                    ? new AggregateInfo(false, null, false, true)
                    : new AggregateInfo(false, null, true, false),
                RealLiteral => new AggregateInfo(false, null, false, true),
                FunctionCall functionCall => AnalyzeFunctionCall(functionCall),
                ParenthesisExpression parenthesis => Analyze(parenthesis.Expression),
                CastCall castCall => Analyze(castCall.Parameter),
                ConvertCall convertCall => AnalyzeConvert(convertCall),
                SimpleCaseExpression caseExpression => AnalyzeCase(caseExpression),
                SearchedCaseExpression searchedCase => AnalyzeSearchedCase(searchedCase),
                BinaryExpression binaryExpression => Merge(Analyze(binaryExpression.FirstExpression), Analyze(binaryExpression.SecondExpression)),
                UnaryExpression unaryExpression => Analyze(unaryExpression.Expression),
                CoalesceExpression coalesceExpression => MergeExpressions(coalesceExpression.Expressions),
                NullIfExpression nullIf => AnalyzeNullIf(nullIf),
                TryCastCall tryCast => Analyze(tryCast.Parameter),
                _ => new AggregateInfo(false, null, false, false)
            };
        }

        private AggregateInfo AnalyzeConvert(ConvertCall convertCall)
        {
            var result = Analyze(convertCall.Parameter);
            if (convertCall.Style is ScalarExpression style)
            {
                result = Merge(result, Analyze(style));
            }

            return result;
        }

        private AggregateInfo AnalyzeNullIf(NullIfExpression nullIf)
        {
            var result = Analyze(nullIf.FirstExpression);
            if (nullIf.SecondExpression != null)
            {
                result = Merge(result, Analyze(nullIf.SecondExpression));
            }
            return result;
        }

        private AggregateInfo AnalyzeFunctionCall(FunctionCall functionCall)
        {
            var name = functionCall.FunctionName?.Value;
            if (!string.IsNullOrWhiteSpace(name) && AggregateNames.Contains(name))
            {
                var combined = new AggregateInfo(true, name.ToLowerInvariant(), false, false);
                foreach (var parameter in functionCall.Parameters)
                {
                    if (parameter is ScalarExpression scalar)
                    {
                        combined = Merge(combined, Analyze(scalar));
                    }
                }

                return combined;
            }

            var result = new AggregateInfo(false, null, false, false);
            foreach (var parameter in functionCall.Parameters)
            {
                if (parameter is ScalarExpression scalar)
                {
                    result = Merge(result, Analyze(scalar));
                }
            }

            return result;
        }

        private AggregateInfo AnalyzeCase(SimpleCaseExpression caseExpression)
        {
            var result = caseExpression.ElseExpression != null
                ? Analyze(caseExpression.ElseExpression)
                : new AggregateInfo(false, null, false, false);
            foreach (var whenClause in caseExpression.WhenClauses)
            {
                if (whenClause?.ThenExpression != null)
                {
                    result = Merge(result, Analyze(whenClause.ThenExpression));
                }
            }
            return result;
        }

        private AggregateInfo AnalyzeSearchedCase(SearchedCaseExpression caseExpression)
        {
            var result = caseExpression.ElseExpression != null
                ? Analyze(caseExpression.ElseExpression)
                : new AggregateInfo(false, null, false, false);
            foreach (var whenClause in caseExpression.WhenClauses)
            {
                if (whenClause?.ThenExpression != null)
                {
                    result = Merge(result, Analyze(whenClause.ThenExpression));
                }
            }
            return result;
        }

        private AggregateInfo MergeExpressions(IList<ScalarExpression> expressions)
        {
            var result = new AggregateInfo(false, null, false, false);
            if (expressions == null)
            {
                return result;
            }

            foreach (var expression in expressions)
            {
                result = Merge(result, Analyze(expression));
            }

            return result;
        }

        private AggregateInfo Merge(AggregateInfo left, AggregateInfo right)
        {
            if (left.IsAggregate)
            {
                var function = left.FunctionName ?? right.FunctionName;
                return new AggregateInfo(true, function, left.HasIntegerLiteral || right.HasIntegerLiteral, left.HasDecimalLiteral || right.HasDecimalLiteral);
            }

            if (right.IsAggregate)
            {
                return new AggregateInfo(true, right.FunctionName, left.HasIntegerLiteral || right.HasIntegerLiteral, left.HasDecimalLiteral || right.HasDecimalLiteral);
            }

            return new AggregateInfo(false, null, left.HasIntegerLiteral || right.HasIntegerLiteral, left.HasDecimalLiteral || right.HasDecimalLiteral);
        }
    }
}

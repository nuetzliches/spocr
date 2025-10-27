using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Augments a <see cref="ProcedureModel"/> by re-analyzing the SQL definition with ScriptDom to capture aggregate metadata.
/// </summary>
internal static class ProcedureModelAggregateAnalyzer
{
    internal sealed record AggregateSummary(
        bool IsAggregate,
        string? FunctionName,
        bool HasIntegerLiteral,
        bool HasDecimalLiteral,
        string? SqlTypeName);

    private sealed record AggregateInfo(
        bool IsAggregate,
        string? FunctionName,
        bool HasIntegerLiteral,
        bool HasDecimalLiteral,
        string? SqlTypeName)
    {
        public static readonly AggregateInfo Empty = new(false, null, false, false, null);
        public static readonly AggregateInfo IntegerLiteral = new(false, null, true, false, null);
        public static readonly AggregateInfo DecimalLiteral = new(false, null, false, true, null);

        public static AggregateInfo ForAggregate(string functionName) => new(true, functionName, false, false, null);
    }

    public static void Apply(string? definition, ProcedureModel? model)
    {
        var fragment = ProcedureModelScriptDomParser.Parse(definition);
        Apply(fragment, model);
    }

    public static void Apply(TSqlFragment? fragment, ProcedureModel? model)
    {
        if (model == null)
        {
            return;
        }

        var summary = CollectAggregateSummaries(fragment);
        if (summary.Count == 0)
        {
            return;
        }

        foreach (var (alias, info) in summary)
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

            if (string.IsNullOrWhiteSpace(column.SqlTypeName) && !string.IsNullOrWhiteSpace(info.SqlTypeName))
            {
                column.SqlTypeName = info.SqlTypeName;
            }
        }
    }

    public static Dictionary<string, AggregateSummary> CollectAggregateSummaries(string? definition)
    {
        var fragment = ProcedureModelScriptDomParser.Parse(definition);
        return CollectAggregateSummaries(fragment);
    }

    public static Dictionary<string, AggregateSummary> CollectAggregateSummaries(TSqlFragment? fragment)
    {
        var result = new Dictionary<string, AggregateSummary>(StringComparer.OrdinalIgnoreCase);
        if (fragment == null)
        {
            return result;
        }

        System.Console.WriteLine($"[agg-debug] fragment={fragment.GetType().Name}");
        var visitor = new AggregateVisitor();
        fragment.Accept(visitor);
        visitor.ResolvePendingColumns();

        System.Console.WriteLine($"[agg-debug] collected={visitor.Columns.Count}");

        foreach (var (alias, info) in visitor.Columns)
        {
            if (!info.IsAggregate)
            {
                continue;
            }

            result[alias] = new AggregateSummary(info.IsAggregate, info.FunctionName, info.HasIntegerLiteral, info.HasDecimalLiteral, info.SqlTypeName);
        }

        return result;
    }

    private static ProcedureResultColumn? FindColumn(ProcedureModel model, string alias)
    {
        if (model.ResultSets == null)
        {
            return null;
        }

        foreach (var resultSet in model.ResultSets)
        {
            var column = FindColumn(resultSet.Columns, alias);
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

        private readonly Stack<QueryContext> _contextStack = new();
        private readonly Dictionary<string, Dictionary<string, AggregateInfo>> _derivedTableColumns = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingColumn> _pendingColumns = new();
        private int _scalarSubqueryDepth;

        public override void ExplicitVisit(TSqlScript node)
        {
            System.Console.WriteLine("[agg-debug] visiting script");
            node.AcceptChildren(this);
        }

        public override void ExplicitVisit(TSqlBatch node)
        {
            System.Console.WriteLine("[agg-debug] visiting batch");
            node.AcceptChildren(this);
        }

        public override void ExplicitVisit(CreateProcedureStatement node)
        {
            System.Console.WriteLine("[agg-debug] visiting create procedure");
            node.AcceptChildren(this);
        }

        public override void ExplicitVisit(SelectStatement node)
        {
            System.Console.WriteLine("[agg-debug] visiting select statement");
            node.AcceptChildren(this);
        }

        public void ResolvePendingColumns()
        {
            if (_pendingColumns.Count == 0)
            {
                return;
            }

            foreach (var pending in _pendingColumns)
            {
                if (!_derivedTableColumns.TryGetValue(pending.DerivedAlias, out var map) || map == null)
                {
                    continue;
                }

                if (!map.TryGetValue(pending.ColumnName, out var info))
                {
                    continue;
                }

                if (pending.IsTopLevel && info.IsAggregate)
                {
                    Columns[pending.Alias] = info;
                }
            }
        }

        public override void ExplicitVisit(QuerySpecification node)
        {
            var isRoot = _contextStack.Count == 0;
            var context = new QueryContext
            {
                DerivedAlias = CurrentDerivedAlias,
                IsTopLevel = isRoot
            };
            _contextStack.Push(context);

            System.Console.WriteLine($"[agg-debug] enter QuerySpecification alias={context.DerivedAlias ?? "<root>"} isRoot={isRoot}");
            node.AcceptChildren(this);

            if (context.SelectColumns.Count > 0 && !string.IsNullOrWhiteSpace(context.DerivedAlias))
            {
                _derivedTableColumns[context.DerivedAlias] = context.SelectColumns;
            }

            _contextStack.Pop();
        }

        public override void ExplicitVisit(ScalarSubquery node)
        {
            _scalarSubqueryDepth++;
            node.AcceptChildren(this);
            _scalarSubqueryDepth--;
        }

        public override void ExplicitVisit(QueryDerivedTable node)
        {
            var previousAlias = CurrentDerivedAlias;
            CurrentDerivedAlias = node.Alias?.Value;
            node.AcceptChildren(this);
            CurrentDerivedAlias = previousAlias;
        }

        public override void ExplicitVisit(SelectScalarExpression node)
        {
            if (_contextStack.Count == 0 || _scalarSubqueryDepth > 0)
            {
                System.Console.WriteLine($"[agg-debug-skip] stack={_contextStack.Count} subDepth={_scalarSubqueryDepth} expr={node.Expression?.GetType().Name}");
                base.ExplicitVisit(node);
                return;
            }

            var alias = GetAlias(node);
            if (string.IsNullOrWhiteSpace(alias))
            {
                base.ExplicitVisit(node);
                return;
            }

            var info = Analyze(node.Expression);

            var current = _contextStack.Peek();
            current.SelectColumns[alias] = info;

            System.Console.WriteLine($"[agg-debug] alias={alias} isTop={current.IsTopLevel} isAgg={info.IsAggregate} expr={node.Expression?.GetType().Name}");

            if (info.IsAggregate && current.IsTopLevel)
            {
                Columns[alias] = info;
            }
            else if (current.IsTopLevel)
            {
                TryResolvePending(alias, node.Expression, current);
            }

            node.AcceptChildren(this);
        }

        private static string? GetAlias(SelectScalarExpression node)
        {
            if (node.ColumnName == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(node.ColumnName.Value))
            {
                return node.ColumnName.Value;
            }

            if (node.ColumnName is IdentifierOrValueExpression ive)
            {
                if (ive.Identifier != null && !string.IsNullOrWhiteSpace(ive.Identifier.Value))
                {
                    return ive.Identifier.Value;
                }

                if (ive.ValueExpression is StringLiteral sl && !string.IsNullOrWhiteSpace(sl.Value))
                {
                    return sl.Value;
                }
            }

            return null;
        }

        private AggregateInfo Analyze(ScalarExpression expression)
        {
            if (expression == null)
            {
                return AggregateInfo.Empty;
            }

            return expression switch
            {
                IntegerLiteral => AggregateInfo.IntegerLiteral,
                NumericLiteral numeric => numeric.Value?.Contains('.') == true
                    ? AggregateInfo.DecimalLiteral
                    : AggregateInfo.IntegerLiteral,
                RealLiteral => AggregateInfo.DecimalLiteral,
                ColumnReferenceExpression columnRef => AnalyzeColumnReference(columnRef),
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
                _ => AggregateInfo.Empty
            };
        }

        private AggregateInfo AnalyzeConvert(ConvertCall convertCall)
        {
            var result = Analyze(convertCall.Parameter);
            if (convertCall.Style is ScalarExpression style)
            {
                result = Merge(result, Analyze(style));
            }

            if (convertCall.DataType is DataTypeReference dataType && dataType.Name?.Identifiers is { Count: > 0 } identifiers)
            {
                var parts = identifiers
                    .Where(id => id != null && !string.IsNullOrWhiteSpace(id.Value))
                    .Select(id => id!.Value);
                var name = string.Join('.', parts);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result = result with { SqlTypeName = name.ToLowerInvariant() };
                }
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

        private AggregateInfo AnalyzeBoolean(BooleanExpression expression)
        {
            if (expression == null)
            {
                return AggregateInfo.Empty;
            }

            return expression switch
            {
                BooleanBinaryExpression binary => Merge(AnalyzeBoolean(binary.FirstExpression), AnalyzeBoolean(binary.SecondExpression)),
                BooleanComparisonExpression comparison => Merge(Analyze(comparison.FirstExpression), Analyze(comparison.SecondExpression)),
                BooleanParenthesisExpression parenthesis => AnalyzeBoolean(parenthesis.Expression),
                BooleanIsNullExpression isNull => Analyze(isNull.Expression),
                BooleanNotExpression notExpression => AnalyzeBoolean(notExpression.Expression),
                InPredicate inPredicate => AnalyzeInPredicate(inPredicate),
                ExistsPredicate exists => AnalyzeExists(exists),
                LikePredicate likePredicate => Merge(Analyze(likePredicate.FirstExpression), Analyze(likePredicate.SecondExpression)),
                _ => AggregateInfo.Empty
            };
        }

        private AggregateInfo AnalyzeInPredicate(InPredicate predicate)
        {
            var result = Analyze(predicate.Expression);
            if (predicate.Values != null)
            {
                foreach (var value in predicate.Values.OfType<ScalarExpression>())
                {
                    result = Merge(result, Analyze(value));
                }
            }
            return result;
        }

        private AggregateInfo AnalyzeExists(ExistsPredicate exists)
        {
            if (exists == null)
            {
                return AggregateInfo.Empty;
            }

            var info = AggregateInfo.ForAggregate("exists");
            var inferred = InferAggregateType("exists", info.HasIntegerLiteral, info.HasDecimalLiteral);
            return info with { SqlTypeName = inferred };
        }

        private AggregateInfo AnalyzeFunctionCall(FunctionCall functionCall)
        {
            var name = functionCall.FunctionName?.Value;
            if (!string.IsNullOrWhiteSpace(name) && AggregateNames.Contains(name))
            {
                var lowered = name.ToLowerInvariant();
                var combined = AggregateInfo.ForAggregate(lowered);
                foreach (var parameter in functionCall.Parameters)
                {
                    if (parameter is ScalarExpression scalar)
                    {
                        combined = Merge(combined, Analyze(scalar));
                    }
                }

                if (string.IsNullOrWhiteSpace(combined.SqlTypeName))
                {
                    combined = combined with { SqlTypeName = InferAggregateType(lowered, combined.HasIntegerLiteral, combined.HasDecimalLiteral) };
                }

                return combined;
            }

            var result = AggregateInfo.Empty;
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
                : AggregateInfo.Empty;
            foreach (var whenClause in caseExpression.WhenClauses)
            {
                if (whenClause?.ThenExpression != null)
                {
                    result = Merge(result, Analyze(whenClause.ThenExpression));
                }
                if (whenClause?.WhenExpression != null)
                {
                    result = Merge(result, Analyze(whenClause.WhenExpression));
                }
            }
            return result;
        }

        private AggregateInfo AnalyzeSearchedCase(SearchedCaseExpression caseExpression)
        {
            var result = caseExpression.ElseExpression != null
                ? Analyze(caseExpression.ElseExpression)
                : AggregateInfo.Empty;
            foreach (var whenClause in caseExpression.WhenClauses)
            {
                if (whenClause?.ThenExpression != null)
                {
                    result = Merge(result, Analyze(whenClause.ThenExpression));
                }
                if (whenClause?.WhenExpression != null)
                {
                    result = Merge(result, AnalyzeBoolean(whenClause.WhenExpression));
                }
            }
            return result;
        }

        private AggregateInfo MergeExpressions(IList<ScalarExpression> expressions)
        {
            var result = AggregateInfo.Empty;
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
                var type = left.SqlTypeName ?? right.SqlTypeName;
                return new AggregateInfo(true, function, left.HasIntegerLiteral || right.HasIntegerLiteral, left.HasDecimalLiteral || right.HasDecimalLiteral, type);
            }

            if (right.IsAggregate)
            {
                var type = right.SqlTypeName ?? left.SqlTypeName;
                return new AggregateInfo(true, right.FunctionName, left.HasIntegerLiteral || right.HasIntegerLiteral, left.HasDecimalLiteral || right.HasDecimalLiteral, type);
            }

            return new AggregateInfo(false, null, left.HasIntegerLiteral || right.HasIntegerLiteral, left.HasDecimalLiteral || right.HasDecimalLiteral, left.SqlTypeName ?? right.SqlTypeName);
        }

        private string? CurrentDerivedAlias { get; set; }

        private static string? InferAggregateType(string functionName, bool hasIntegerLiteral, bool hasDecimalLiteral)
        {
            return functionName switch
            {
                "count" => "int",
                "count_big" => "bigint",
                "exists" => "bit",
                "avg" => "decimal(18,2)",
                "sum" => hasDecimalLiteral
                    ? "decimal(18,2)"
                    : hasIntegerLiteral ? "int" : "decimal(18,2)",
                _ => null
            };
        }

        private sealed class QueryContext
        {
            public Dictionary<string, AggregateInfo> SelectColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string? DerivedAlias { get; set; }
            public bool IsTopLevel { get; set; }
        }

        private void TryResolvePending(string alias, ScalarExpression? expression, QueryContext current)
        {
            if (expression is not ColumnReferenceExpression columnRef)
            {
                return;
            }

            if (columnRef.MultiPartIdentifier?.Identifiers is not { Count: > 0 } identifiers)
            {
                return;
            }

            var derivedAlias = identifiers[0]?.Value;
            var columnName = identifiers[^1]?.Value;
            if (string.IsNullOrWhiteSpace(derivedAlias) || string.IsNullOrWhiteSpace(columnName))
            {
                return;
            }

            if (_derivedTableColumns.TryGetValue(derivedAlias, out var map) && map != null && map.TryGetValue(columnName, out var resolved))
            {
                if (resolved.IsAggregate)
                {
                    Columns[alias] = resolved;
                    current.SelectColumns[alias] = resolved;
                }
                return;
            }

            _pendingColumns.Add(new PendingColumn(alias, derivedAlias, columnName, current.IsTopLevel));
        }

        private AggregateInfo AnalyzeColumnReference(ColumnReferenceExpression columnRef)
        {
            if (columnRef.MultiPartIdentifier?.Identifiers is not { Count: > 0 } identifiers)
            {
                return AggregateInfo.Empty;
            }

            var alias = identifiers[0]?.Value;
            if (string.IsNullOrWhiteSpace(alias))
            {
                return AggregateInfo.Empty;
            }

            if (_derivedTableColumns.TryGetValue(alias, out var map) && map != null && map.Count > 0)
            {
                var leaf = identifiers[^1]?.Value;
                if (!string.IsNullOrWhiteSpace(leaf) && map.TryGetValue(leaf, out var info))
                {
                    return info;
                }
            }

            return AggregateInfo.Empty;
        }

        private sealed record PendingColumn(string Alias, string DerivedAlias, string ColumnName, bool IsTopLevel);
    }
}

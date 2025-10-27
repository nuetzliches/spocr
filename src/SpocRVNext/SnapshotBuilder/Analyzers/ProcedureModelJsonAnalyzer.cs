using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Derives JSON-specific metadata for <see cref="ProcedureModel"/> instances by re-parsing the SQL definition with ScriptDom.
/// </summary>
internal static class ProcedureModelJsonAnalyzer
{
    private sealed record JsonProjectionInfo(bool ReturnsJson, bool? ReturnsJsonArray, string? RootProperty, bool IsNested);

    public static void Apply(string? definition, ProcedureModel? model)
    {
        var fragment = ProcedureModelScriptDomParser.Parse(definition);
        Apply(fragment, model, definition);
    }

    public static void Apply(TSqlFragment? fragment, ProcedureModel? model)
    {
        Apply(fragment, model, null);
    }

    public static void Apply(TSqlFragment? fragment, ProcedureModel? model, string? definition)
    {
        if (fragment == null || model == null)
        {
            return;
        }

        var visitor = new JsonVisitor(definition);
        fragment.Accept(visitor);

        if (model.ResultSets.Count > 0 && visitor.TopLevelJson.Count > 0)
        {
            foreach (var (index, info) in visitor.TopLevelJson)
            {
                if (index < 0 || index >= model.ResultSets.Count)
                {
                    continue;
                }

                var resultSet = model.ResultSets[index];
                resultSet.ReturnsJson = info.ReturnsJson;
                if (info.ReturnsJsonArray.HasValue)
                {
                    resultSet.ReturnsJsonArray = info.ReturnsJsonArray.Value;
                }

                if (!string.IsNullOrWhiteSpace(info.RootProperty) && string.IsNullOrWhiteSpace(resultSet.JsonRootProperty))
                {
                    resultSet.JsonRootProperty = info.RootProperty;
                }
            }
        }

        if (visitor.NestedJson.Count == 0)
        {
            return;
        }

        foreach (var resultSet in model.ResultSets)
        {
            ApplyNested(resultSet.Columns, visitor.NestedJson);
        }
    }

    private static void ApplyNested(IReadOnlyList<ProcedureResultColumn> columns, IReadOnlyDictionary<string, JsonProjectionInfo> map)
    {
        if (columns == null)
        {
            return;
        }

        foreach (var column in columns)
        {
            if (column == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(column.Name))
            {
                var normalized = NormalizeAlias(column.Name);
                if (map.TryGetValue(normalized, out var info))
                {
                    column.ReturnsJson = info.ReturnsJson;
                    if (info.IsNested)
                    {
                        column.IsNestedJson = true;
                    }

                    if (info.ReturnsJsonArray.HasValue)
                    {
                        column.ReturnsJsonArray = info.ReturnsJsonArray.Value;
                    }

                    if (!string.IsNullOrWhiteSpace(info.RootProperty) && string.IsNullOrWhiteSpace(column.JsonRootProperty))
                    {
                        column.JsonRootProperty = info.RootProperty;
                    }
                }
            }

            if (column.Columns != null && column.Columns.Count > 0)
            {
                ApplyNested(column.Columns, map);
            }
        }
    }

    private static string NormalizeAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return string.Empty;
        }

        var trimmed = alias.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private sealed class JsonVisitor : TSqlFragmentVisitor
    {
        private static readonly Regex RootRegex = new("ROOT\\s*\\(\\s*'([^']+)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly string? _definition;
        private int _queryDepth;
        private int _scalarSubqueryDepth;
        private int _topLevelQueryIndex;

        public JsonVisitor(string? definition)
        {
            _definition = definition;
        }

        public Dictionary<int, JsonProjectionInfo> TopLevelJson { get; } = new();
        public Dictionary<string, JsonProjectionInfo> NestedJson { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void ExplicitVisit(ScalarSubquery node)
        {
            _scalarSubqueryDepth++;
            base.ExplicitVisit(node);
            _scalarSubqueryDepth--;
        }

        public override void ExplicitVisit(QuerySpecification node)
        {
            var isTopLevel = _scalarSubqueryDepth == 0 && _queryDepth == 0;
            _queryDepth++;

            if (isTopLevel)
            {
                var info = ExtractForJson(node);
                if (info != null)
                {
                    TopLevelJson[_topLevelQueryIndex] = info;
                }

                _topLevelQueryIndex++;
            }

            base.ExplicitVisit(node);
            _queryDepth--;
        }

        public override void ExplicitVisit(SelectScalarExpression node)
        {
            var alias = node.ColumnName?.Value;
            if (!string.IsNullOrWhiteSpace(alias) && _queryDepth == 1 && _scalarSubqueryDepth == 0)
            {
                JsonProjectionInfo? info = null;

                if (node.Expression is ScalarSubquery subquery && subquery.QueryExpression is QuerySpecification qs)
                {
                    info = ExtractForJson(qs, isNested: true);
                    if (info != null)
                    {
                        info = info with { IsNested = true };
                    }
                }
                else if (node.Expression is FunctionCall call && IsJsonQuery(call))
                {
                    info = new JsonProjectionInfo(true, null, null, true);
                }

                if (info != null)
                {
                    var normalized = NormalizeAlias(alias);
                    if (!NestedJson.ContainsKey(normalized))
                    {
                        NestedJson[normalized] = info;
                    }
                }
            }

            base.ExplicitVisit(node);
        }

        private static bool IsJsonQuery(FunctionCall call)
        {
            return string.Equals(call?.FunctionName?.Value, "JSON_QUERY", StringComparison.OrdinalIgnoreCase);
        }

        private JsonProjectionInfo? ExtractForJson(QuerySpecification node, bool isNested = false)
        {
            if (node.ForClause is JsonForClause jsonClause)
            {
                bool withoutArrayWrapper = false;
                string? root = null;
                bool? returnsArray = null;

                var options = jsonClause.Options ?? Array.Empty<JsonForClauseOption>();
                if (options.Count == 0)
                {
                    returnsArray = true;
                }

                foreach (var option in options)
                {
                    switch (option.OptionKind)
                    {
                        case JsonForClauseOptions.WithoutArrayWrapper:
                            withoutArrayWrapper = true;
                            break;
                        case JsonForClauseOptions.Root:
                            if (root == null && option.Value is Literal literal)
                            {
                                root = ExtractLiteralValue(literal);
                            }
                            break;
                        default:
                            returnsArray ??= true;
                            break;
                    }
                }

                JsonProjectionInfo? fallback = null;
                if (root == null || !returnsArray.HasValue)
                {
                    fallback = ExtractViaSegment(node, isNested);
                }

                var array = withoutArrayWrapper
                    ? false
                    : (returnsArray ?? fallback?.ReturnsJsonArray ?? true);
                var effectiveRoot = root ?? fallback?.RootProperty;

                return new JsonProjectionInfo(true, array, effectiveRoot, isNested);
            }

            return ExtractViaSegment(node, isNested);
        }

        private JsonProjectionInfo? ExtractViaSegment(TSqlFragment fragment, bool isNested)
        {
            if (string.IsNullOrEmpty(_definition))
            {
                return null;
            }

            var segment = GetSegment(fragment);
            if (string.IsNullOrWhiteSpace(segment))
            {
                return null;
            }

            if (segment.IndexOf("FOR JSON", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var withoutArray = segment.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;
            string? root = null;
            var match = RootRegex.Match(segment);
            if (match.Success && match.Groups.Count > 1)
            {
                root = match.Groups[1].Value;
            }

            var returnsArray = withoutArray ? false : true;
            return new JsonProjectionInfo(true, returnsArray, root, isNested);
        }

        private string GetSegment(TSqlFragment fragment)
        {
            if (fragment == null || fragment.StartOffset < 0 || fragment.FragmentLength <= 0 || string.IsNullOrEmpty(_definition))
            {
                return string.Empty;
            }

            var start = Math.Max(0, fragment.StartOffset);
            var end = fragment.StartOffset >= 0 && fragment.FragmentLength > 0
                ? Math.Min(_definition!.Length, fragment.StartOffset + fragment.FragmentLength + 200)
                : _definition!.Length;
            if (end <= start)
            {
                return string.Empty;
            }

            return _definition!.Substring(start, end - start);
        }

        private static string? ExtractLiteralValue(Literal literal)
        {
            return literal switch
            {
                StringLiteral sl when !string.IsNullOrWhiteSpace(sl.Value) => sl.Value,
                IntegerLiteral il when !string.IsNullOrWhiteSpace(il.Value) => il.Value,
                NumericLiteral nl when !string.IsNullOrWhiteSpace(nl.Value) => nl.Value,
                _ => null
            };
        }
    }
}

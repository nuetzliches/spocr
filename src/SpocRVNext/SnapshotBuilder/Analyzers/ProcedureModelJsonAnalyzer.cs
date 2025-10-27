using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Derives JSON-specific metadata for <see cref="ProcedureModel"/> instances by re-parsing the SQL definition with ScriptDom.
/// </summary>
internal static class ProcedureModelJsonAnalyzer
{
    private sealed record JsonProjectionInfo(bool ReturnsJson, bool? ReturnsJsonArray, string? RootProperty, bool IsNested);

    private static readonly PropertyInfo? ForClauseProperty = typeof(QuerySpecification).GetProperty("ForClause", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

        var visitor = new JsonVisitor();
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
        private int _queryDepth;
        private int _scalarSubqueryDepth;
        private int _topLevelQueryIndex;

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
                    info = ExtractForJson(qs);
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

        private static JsonProjectionInfo? ExtractForJson(QuerySpecification node)
        {
            var clause = GetForClause(node);
            if (clause == null || !IsForJsonClause(clause))
            {
                return null;
            }

            var withoutArray = GetBooleanProperty(clause, "WithoutArrayWrapper") ?? false;
            var root = GetRootName(clause);
            var returnsArray = withoutArray ? false : true;

            return new JsonProjectionInfo(true, returnsArray, root, false);
        }

        private static object? GetForClause(QuerySpecification node)
        {
            return ForClauseProperty?.GetValue(node);
        }

        private static bool IsForJsonClause(object clause)
        {
            if (clause == null)
            {
                return false;
            }

            var typeName = clause.GetType().Name;
            return typeName.IndexOf("ForJson", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool? GetBooleanProperty(object clause, string propertyName)
        {
            var property = clause.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                return null;
            }

            var value = property.GetValue(clause);
            return value is bool b ? b : null;
        }

        private static string? GetRootName(object clause)
        {
            var property = clause.GetType().GetProperty("RootName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                return null;
            }

            var rootIdentifier = property.GetValue(clause);
            if (rootIdentifier == null)
            {
                return null;
            }

            var valueProperty = rootIdentifier.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return valueProperty?.GetValue(rootIdentifier) as string;
        }
    }
}

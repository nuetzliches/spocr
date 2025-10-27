using System;
using System.Text.RegularExpressions;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Applies lightweight normalizations to the intermediate procedure model until dedicated analyzers replace the legacy parser.
/// </summary>
internal static class ProcedureModelPostProcessor
{
    private static readonly Regex AggregateRegex = new("\\b(count_big|count|sum|avg|min|max|exists)\\s*\\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Apply(ProcedureModel? model)
    {
        if (model == null)
        {
            return;
        }

        foreach (var resultSet in model.ResultSets)
        {
            if (resultSet?.Columns == null) continue;
            foreach (var column in resultSet.Columns)
            {
                NormalizeColumn(column);
            }
        }
    }

    private static void NormalizeColumn(ProcedureResultColumn? column)
    {
        if (column == null)
        {
            return;
        }

        if (column.Columns != null && column.Columns.Count > 0)
        {
            foreach (var child in column.Columns)
            {
                NormalizeColumn(child);
            }
        }

        EnsureAggregate(column);
    }

    private static void EnsureAggregate(ProcedureResultColumn column)
    {
        var aggregateFunction = NormalizeAggregateName(column.AggregateFunction);
        var isAggregate = column.IsAggregate || aggregateFunction != null;

        if (!isAggregate)
        {
            aggregateFunction = DetectAggregate(column.RawExpression) ?? aggregateFunction;
            if (aggregateFunction != null)
            {
                isAggregate = true;
            }
        }
        else if (aggregateFunction == null)
        {
            aggregateFunction = DetectAggregate(column.RawExpression);
        }

        column.IsAggregate = isAggregate;
        if (aggregateFunction != null)
        {
            column.AggregateFunction = aggregateFunction;
        }

        if (column.IsAggregate)
        {
            ApplyAggregateTypeHeuristics(column);
        }
    }

    private static string? NormalizeAggregateName(string? aggregate)
    {
        if (string.IsNullOrWhiteSpace(aggregate))
        {
            return null;
        }

        var trimmed = aggregate.Trim();
        return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
    }

    private static string? DetectAggregate(string? rawExpression)
    {
        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            return null;
        }

        var match = AggregateRegex.Match(rawExpression);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static void ApplyAggregateTypeHeuristics(ProcedureResultColumn column)
    {
        if (string.IsNullOrWhiteSpace(column.AggregateFunction))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(column.SqlTypeName))
        {
            return;
        }

        switch (column.AggregateFunction)
        {
            case "count":
                column.SqlTypeName = "int";
                column.IsNullable ??= false;
                break;
            case "count_big":
                column.SqlTypeName = "bigint";
                column.IsNullable ??= false;
                break;
            case "exists":
                column.SqlTypeName = "bit";
                column.IsNullable ??= false;
                break;
            case "avg":
                column.SqlTypeName = "decimal(18,2)";
                column.IsNullable ??= true;
                break;
            case "sum":
                if (column.HasIntegerLiteral && !column.HasDecimalLiteral)
                {
                    column.SqlTypeName = "int";
                }
                else if (column.HasDecimalLiteral)
                {
                    column.SqlTypeName = "decimal(18,4)";
                }
                else
                {
                    column.SqlTypeName = "decimal(18,2)";
                }
                break;
            case "min":
            case "max":
                if (column.HasIntegerLiteral && !column.HasDecimalLiteral)
                {
                    column.SqlTypeName = "int";
                }
                else if (column.HasDecimalLiteral)
                {
                    column.SqlTypeName = "decimal(18,2)";
                }
                break;
        }
    }
}

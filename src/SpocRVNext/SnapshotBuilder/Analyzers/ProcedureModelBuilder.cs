using SpocR.Models;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Builds <see cref="ProcedureModel"/> instances from SQL definitions.
/// </summary>
internal interface IProcedureModelBuilder
{
    ProcedureModel? Build(string? definition, string? defaultSchema, bool verboseParsing);
}

/// <summary>
/// Temporary adapter that uses <see cref="StoredProcedureContentModel"/> to materialize <see cref="ProcedureModel"/> instances
/// while the legacy dependency is still in place. This allows us to migrate call sites without leaking legacy types.
/// </summary>
internal sealed class LegacyProcedureModelBuilder : IProcedureModelBuilder
{
    public ProcedureModel? Build(string? definition, string? defaultSchema, bool verboseParsing)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        StoredProcedureContentModel.SetAstVerbose(verboseParsing);
        var normalizedSchema = string.IsNullOrWhiteSpace(defaultSchema) ? "dbo" : defaultSchema;
        var legacyModel = StoredProcedureContentModel.Parse(definition, normalizedSchema);
        return ConvertToProcedureModel(legacyModel);
    }

    private static ProcedureModel? ConvertToProcedureModel(StoredProcedureContentModel? source)
    {
        if (source == null)
        {
            return null;
        }

        var model = new ProcedureModel();

        if (source.ExecutedProcedures != null)
        {
            foreach (var executed in source.ExecutedProcedures)
            {
                var mapped = ConvertExecutedProcedure(executed);
                if (mapped != null)
                {
                    model.ExecutedProcedures.Add(mapped);
                }
            }
        }

        if (source.ResultSets != null)
        {
            foreach (var resultSet in source.ResultSets)
            {
                var mapped = ConvertResultSet(resultSet);
                if (mapped != null)
                {
                    model.ResultSets.Add(mapped);
                }
            }
        }

        return model;
    }

    private static ProcedureExecutedProcedureCall? ConvertExecutedProcedure(StoredProcedureContentModel.ExecutedProcedureCall? source)
    {
        if (source == null)
        {
            return null;
        }

        return new ProcedureExecutedProcedureCall
        {
            Schema = source.Schema,
            Name = source.Name,
            IsCaptured = source.IsCaptured
        };
    }

    private static ProcedureResultSet? ConvertResultSet(StoredProcedureContentModel.ResultSet? source)
    {
        if (source == null)
        {
            return null;
        }

        var target = new ProcedureResultSet
        {
            ReturnsJson = source.ReturnsJson,
            ReturnsJsonArray = source.ReturnsJsonArray,
            JsonRootProperty = source.JsonRootProperty,
            HasSelectStar = source.HasSelectStar,
            ExecSourceSchemaName = source.ExecSourceSchemaName,
            ExecSourceProcedureName = source.ExecSourceProcedureName,
            Reference = ConvertReference(source.Reference)
        };

        if (source.Columns != null)
        {
            foreach (var column in source.Columns)
            {
                var mapped = ConvertResultColumn(column);
                if (mapped != null)
                {
                    target.Columns.Add(mapped);
                }
            }
        }

        return target;
    }

    private static ProcedureResultColumn? ConvertResultColumn(StoredProcedureContentModel.ResultColumn? source)
    {
        if (source == null)
        {
            return null;
        }

        var target = new ProcedureResultColumn
        {
            Name = source.Name,
            ExpressionKind = ConvertExpressionKind(source.ExpressionKind),
            SourceSchema = source.SourceSchema,
            SourceTable = source.SourceTable,
            SourceColumn = source.SourceColumn,
            SourceAlias = source.SourceAlias,
            SqlTypeName = source.SqlTypeName,
            CastTargetType = source.CastTargetType,
            CastTargetLength = source.CastTargetLength,
            CastTargetPrecision = source.CastTargetPrecision,
            CastTargetScale = source.CastTargetScale,
            HasIntegerLiteral = source.HasIntegerLiteral,
            HasDecimalLiteral = source.HasDecimalLiteral,
            IsNullable = source.IsNullable,
            ForcedNullable = source.ForcedNullable,
            IsNestedJson = source.IsNestedJson,
            ReturnsJson = source.ReturnsJson,
            ReturnsJsonArray = source.ReturnsJsonArray,
            JsonRootProperty = source.JsonRootProperty,
            UserTypeSchemaName = source.UserTypeSchemaName,
            UserTypeName = source.UserTypeName,
            MaxLength = source.MaxLength,
            IsAmbiguous = source.IsAmbiguous,
            RawExpression = source.RawExpression,
            IsAggregate = source.IsAggregate,
            AggregateFunction = source.AggregateFunction,
            Reference = ConvertReference(source.Reference),
            DeferredJsonExpansion = source.DeferredJsonExpansion
        };

        if (source.Columns != null)
        {
            foreach (var child in source.Columns)
            {
                var mappedChild = ConvertResultColumn(child);
                if (mappedChild != null)
                {
                    target.Columns.Add(mappedChild);
                }
            }
        }

        return target;
    }

    private static ProcedureReference? ConvertReference(StoredProcedureContentModel.ColumnReferenceInfo? source)
    {
        if (source == null)
        {
            return null;
        }

        return new ProcedureReference
        {
            Kind = source.Kind,
            Schema = source.Schema,
            Name = source.Name
        };
    }

    private static ProcedureResultColumnExpressionKind ConvertExpressionKind(StoredProcedureContentModel.ResultColumnExpressionKind? kind)
    {
        return kind switch
        {
            StoredProcedureContentModel.ResultColumnExpressionKind.ColumnRef => ProcedureResultColumnExpressionKind.ColumnRef,
            StoredProcedureContentModel.ResultColumnExpressionKind.Cast => ProcedureResultColumnExpressionKind.Cast,
            StoredProcedureContentModel.ResultColumnExpressionKind.FunctionCall => ProcedureResultColumnExpressionKind.FunctionCall,
            StoredProcedureContentModel.ResultColumnExpressionKind.JsonQuery => ProcedureResultColumnExpressionKind.JsonQuery,
            StoredProcedureContentModel.ResultColumnExpressionKind.Computed => ProcedureResultColumnExpressionKind.Computed,
            _ => ProcedureResultColumnExpressionKind.Unknown
        };
    }
}

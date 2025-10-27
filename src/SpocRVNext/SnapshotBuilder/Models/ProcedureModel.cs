using System.Collections.Generic;

namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

/// <summary>
/// Represents the parsed structure of a stored procedure relevant for snapshot generation.
/// </summary>
public sealed class ProcedureModel
{
    public List<ProcedureExecutedProcedureCall> ExecutedProcedures { get; } = new();
    public List<ProcedureResultSet> ResultSets { get; } = new();
}

public sealed class ProcedureExecutedProcedureCall
{
    public string? Schema { get; set; }
    public string? Name { get; set; }
    public bool IsCaptured { get; set; }
}

public sealed class ProcedureResultSet
{
    public bool ReturnsJson { get; set; }
    public bool ReturnsJsonArray { get; set; }
    public string? JsonRootProperty { get; set; }
    public bool HasSelectStar { get; set; }
    public string? ExecSourceSchemaName { get; set; }
    public string? ExecSourceProcedureName { get; set; }
    public ProcedureReference? Reference { get; set; }
    public List<ProcedureResultColumn> Columns { get; } = new();
}

public sealed class ProcedureResultColumn
{
    public string? Name { get; set; }
    public ProcedureResultColumnExpressionKind ExpressionKind { get; set; }
    public string? SourceSchema { get; set; }
    public string? SourceTable { get; set; }
    public string? SourceColumn { get; set; }
    public string? SourceAlias { get; set; }
    public string? SqlTypeName { get; set; }
    public string? CastTargetType { get; set; }
    public int? CastTargetLength { get; set; }
    public int? CastTargetPrecision { get; set; }
    public int? CastTargetScale { get; set; }
    public bool HasIntegerLiteral { get; set; }
    public bool HasDecimalLiteral { get; set; }
    public bool? IsNullable { get; set; }
    public bool? ForcedNullable { get; set; }
    public bool? IsNestedJson { get; set; }
    public bool? ReturnsJson { get; set; }
    public bool? ReturnsJsonArray { get; set; }
    public string? JsonRootProperty { get; set; }
    public List<ProcedureResultColumn> Columns { get; } = new();
    public string? UserTypeSchemaName { get; set; }
    public string? UserTypeName { get; set; }
    public int? MaxLength { get; set; }
    public bool? IsAmbiguous { get; set; }
    public string? RawExpression { get; set; }
    public bool IsAggregate { get; set; }
    public string? AggregateFunction { get; set; }
    public ProcedureReference? Reference { get; set; }
    public bool? DeferredJsonExpansion { get; set; }
}

public sealed class ProcedureReference
{
    public string? Kind { get; set; }
    public string? Schema { get; set; }
    public string? Name { get; set; }
}

public enum ProcedureResultColumnExpressionKind
{
    Unknown = 0,
    ColumnRef,
    Cast,
    FunctionCall,
    JsonQuery,
    Computed
}

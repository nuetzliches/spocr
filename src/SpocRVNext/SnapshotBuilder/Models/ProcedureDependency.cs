using System;

namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

public enum ProcedureDependencyKind
{
    Unknown = 0,
    Procedure,
    Function,
    View,
    Table,
    UserDefinedTableType,
    UserDefinedType
}

public sealed class ProcedureDependency
{
    public ProcedureDependencyKind Kind { get; init; } = ProcedureDependencyKind.Unknown;
    public string Schema { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTime? LastModifiedUtc { get; init; }

    public override string ToString()
    {
        var identifier = string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
        return Kind == ProcedureDependencyKind.Unknown ? identifier : $"{Kind}:{identifier}";
    }
}

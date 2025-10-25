namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

/// <summary>
/// Minimal identifier for a stored procedure. Additional metadata can be layered without touching stage contracts.
/// </summary>
public sealed class ProcedureDescriptor
{
    public string Schema { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}

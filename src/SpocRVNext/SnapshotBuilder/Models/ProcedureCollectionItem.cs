namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

public sealed class ProcedureCollectionItem
{
    public ProcedureDescriptor Descriptor { get; init; } = new();
    public ProcedureCollectionDecision Decision { get; init; } = ProcedureCollectionDecision.Unknown;
}

using System.Collections.Generic;

namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

public sealed class ProcedureCollectionResult
{
    public IReadOnlyList<ProcedureCollectionItem> Items { get; init; } = new List<ProcedureCollectionItem>();
}

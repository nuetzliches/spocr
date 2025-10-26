using System;
using System.Collections.Generic;

namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

public sealed class ProcedureCollectionItem
{
    public ProcedureDescriptor Descriptor { get; init; } = new();
    public ProcedureCollectionDecision Decision { get; init; } = ProcedureCollectionDecision.Unknown;
    public DateTime? LastModifiedUtc { get; init; }
    public string? CachedSnapshotHash { get; init; }
    public string? CachedSnapshotFile { get; init; }
    public IReadOnlyList<ProcedureDependency> CachedDependencies { get; init; } = Array.Empty<ProcedureDependency>();
}

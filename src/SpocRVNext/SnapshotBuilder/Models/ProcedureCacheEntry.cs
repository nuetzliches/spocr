using System;
using System.Collections.Generic;

namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

/// <summary>
/// Represents a cached fingerprint for a stored procedure snapshot.
/// </summary>
public sealed class ProcedureCacheEntry
{
    public string Schema { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTime LastModifiedUtc { get; init; }
    public string? SnapshotHash { get; init; }
    public string? SnapshotFile { get; init; }
    public DateTime LastAnalyzedUtc { get; init; }
    public IReadOnlyList<ProcedureDependency> Dependencies { get; init; } = Array.Empty<ProcedureDependency>();

    public override string ToString() => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}

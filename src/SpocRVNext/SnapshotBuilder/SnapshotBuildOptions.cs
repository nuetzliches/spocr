using System;
using System.Collections.Generic;

namespace SpocR.SpocRVNext.SnapshotBuilder;

/// <summary>
/// Represents orchestration-level options for the snapshot builder pipeline.
/// Treated as immutable during a single run to simplify coordination between stages.
/// </summary>
public sealed class SnapshotBuildOptions
{
    public IReadOnlyList<string> Schemas { get; init; } = Array.Empty<string>();
    public string? ProcedureWildcard { get; init; }
    public bool NoCache { get; init; }
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
    public bool Verbose { get; init; }

    public static SnapshotBuildOptions Default => new();
}

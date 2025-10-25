using System.Collections.Generic;

namespace SpocR.SpocRVNext.SnapshotBuilder;

/// <summary>
/// Aggregated outcome for a snapshot build run. Useful for CLI reporting and tests.
/// </summary>
public sealed class SnapshotBuildResult
{
    public int ProceduresAnalyzed { get; init; }
    public int ProceduresSkipped { get; init; }
    public int FilesWritten { get; init; }
    public int FilesUnchanged { get; init; }
    public IReadOnlyDictionary<string, string>? Diagnostics { get; init; }
}

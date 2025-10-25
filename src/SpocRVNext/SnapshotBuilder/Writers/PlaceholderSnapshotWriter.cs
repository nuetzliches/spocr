using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

/// <summary>
/// Placeholder writer that reports no file changes. Replaced with streaming writer later in EPIC-E015.
/// </summary>
internal sealed class PlaceholderSnapshotWriter : ISnapshotWriter
{
    public Task<SnapshotWriteResult> WriteAsync(IReadOnlyList<ProcedureAnalysisResult> analyzedProcedures, SnapshotBuildOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new SnapshotWriteResult
        {
            FilesWritten = 0,
            FilesUnchanged = analyzedProcedures?.Count ?? 0
        };
        return Task.FromResult(result);
    }
}

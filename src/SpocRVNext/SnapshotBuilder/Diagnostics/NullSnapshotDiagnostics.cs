using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Diagnostics;

/// <summary>
/// Diagnostics sink that swallows all notifications (default for CLI-less scenarios).
/// </summary>
internal sealed class NullSnapshotDiagnostics : ISnapshotDiagnostics
{
    public ValueTask OnCollectionCompletedAsync(ProcedureCollectionResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask OnAnalysisCompletedAsync(int analyzedCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask OnWriteCompletedAsync(SnapshotWriteResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask OnTelemetryAsync(SnapshotPhaseTelemetry telemetry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

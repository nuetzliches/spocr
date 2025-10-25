using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Diagnostics;

/// <summary>
/// Lightweight hook for observing orchestration progress without hard-coding console output.
/// </summary>
public interface ISnapshotDiagnostics
{
    ValueTask OnCollectionCompletedAsync(ProcedureCollectionResult result, CancellationToken cancellationToken);
    ValueTask OnAnalysisCompletedAsync(int analyzedCount, CancellationToken cancellationToken);
    ValueTask OnWriteCompletedAsync(Models.SnapshotWriteResult result, CancellationToken cancellationToken);
    ValueTask OnTelemetryAsync(SnapshotPhaseTelemetry telemetry, CancellationToken cancellationToken);
}

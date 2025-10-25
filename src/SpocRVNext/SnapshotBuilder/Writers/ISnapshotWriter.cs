using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

public interface ISnapshotWriter
{
    Task<SnapshotWriteResult> WriteAsync(IReadOnlyList<ProcedureAnalysisResult> analyzedProcedures, SnapshotBuildOptions options, CancellationToken cancellationToken);
}

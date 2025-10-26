using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Cache;

public interface ISnapshotCache
{
    Task InitializeAsync(SnapshotBuildOptions options, CancellationToken cancellationToken);
    ProcedureCacheEntry? TryGetProcedure(ProcedureDescriptor descriptor);
    Task RecordReuseAsync(ProcedureCollectionItem item, CancellationToken cancellationToken);
    Task RecordAnalysisAsync(ProcedureAnalysisResult result, CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);
}

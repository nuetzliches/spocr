using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Collectors;

public interface IProcedureCollector
{
    Task<ProcedureCollectionResult> CollectAsync(SnapshotBuildOptions options, CancellationToken cancellationToken);
}

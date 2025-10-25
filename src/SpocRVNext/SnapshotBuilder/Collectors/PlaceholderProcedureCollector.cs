using System;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Collectors;

/// <summary>
/// Temporary collector that returns an empty result. Replaced once DB enumeration is implemented.
/// </summary>
internal sealed class PlaceholderProcedureCollector : IProcedureCollector
{
    public Task<ProcedureCollectionResult> CollectAsync(SnapshotBuildOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new ProcedureCollectionResult
        {
            Items = Array.Empty<ProcedureCollectionItem>()
        };
        return Task.FromResult(result);
    }
}

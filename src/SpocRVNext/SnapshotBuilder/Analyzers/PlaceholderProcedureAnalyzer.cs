using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Placeholder analyzer that performs no work yet.
/// </summary>
internal sealed class PlaceholderProcedureAnalyzer : IProcedureAnalyzer
{
    public Task<IReadOnlyList<ProcedureAnalysisResult>> AnalyzeAsync(
        IReadOnlyList<ProcedureCollectionItem> items,
        SnapshotBuildOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ProcedureAnalysisResult> empty = Array.Empty<ProcedureAnalysisResult>();
        return Task.FromResult(empty);
    }
}

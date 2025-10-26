using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.SpocRVNext.Utils;

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

        if (items == null || items.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ProcedureAnalysisResult>>(Array.Empty<ProcedureAnalysisResult>());
        }

        var results = new List<ProcedureAnalysisResult>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = item.Descriptor;
            var fallbackFile = BuildSnapshotFileName(descriptor);
            results.Add(new ProcedureAnalysisResult
            {
                Descriptor = descriptor,
                WasReusedFromCache = false,
                SourceLastModifiedUtc = item.LastModifiedUtc,
                SnapshotHash = null,
                SnapshotFile = item.CachedSnapshotFile ?? fallbackFile,
                Dependencies = Array.Empty<ProcedureDependency>()
            });
        }

        return Task.FromResult<IReadOnlyList<ProcedureAnalysisResult>>(results);
    }

    private static string BuildSnapshotFileName(ProcedureDescriptor descriptor)
    {
        var schema = NameSanitizer.SanitizeForFile(descriptor?.Schema ?? string.Empty);
        var name = NameSanitizer.SanitizeForFile(descriptor?.Name ?? string.Empty);
        return string.IsNullOrWhiteSpace(schema)
            ? string.IsNullOrWhiteSpace(name) ? "procedure.json" : $"{name}.json"
            : $"{schema}.{name}.json";
    }
}

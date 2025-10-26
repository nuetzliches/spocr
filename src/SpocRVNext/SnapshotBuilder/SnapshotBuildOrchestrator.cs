using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Analyzers;
using SpocR.SpocRVNext.SnapshotBuilder.Cache;
using SpocR.SpocRVNext.SnapshotBuilder.Collectors;
using SpocR.SpocRVNext.SnapshotBuilder.Diagnostics;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.SpocRVNext.SnapshotBuilder.Writers;

namespace SpocR.SpocRVNext.SnapshotBuilder;

/// <summary>
/// Coordinates the snapshot build pipeline. Concrete stages are injected to keep the orchestrator testable and light-weight.
/// </summary>
public sealed class SnapshotBuildOrchestrator
{
    private readonly IProcedureCollector _collector;
    private readonly IProcedureAnalyzer _analyzer;
    private readonly ISnapshotWriter _writer;
    private readonly ISnapshotCache _cache;
    private readonly ISnapshotDiagnostics _diagnostics;

    public SnapshotBuildOrchestrator(
        IProcedureCollector collector,
        IProcedureAnalyzer analyzer,
        ISnapshotWriter writer,
        ISnapshotCache cache,
        ISnapshotDiagnostics diagnostics)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<SnapshotBuildResult> RunAsync(SnapshotBuildOptions options, CancellationToken cancellationToken = default)
    {
        options ??= SnapshotBuildOptions.Default;
        await _cache.InitializeAsync(options, cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        var collectionResult = await _collector.CollectAsync(options, cancellationToken).ConfigureAwait(false);
        var selectedDescriptors = collectionResult.Items
            .Where(static i => i.Decision == ProcedureCollectionDecision.Analyze || i.Decision == ProcedureCollectionDecision.Reuse)
            .Select(static i => new ProcedureDescriptor
            {
                Schema = i.Descriptor.Schema,
                Name = i.Descriptor.Name
            })
            .ToArray();
        stopwatch.Stop();
        var reusedItems = collectionResult.Items
            .Where(static i => i.Decision == ProcedureCollectionDecision.Reuse)
            .ToArray();
        foreach (var reused in reusedItems)
        {
            await _cache.RecordReuseAsync(reused, cancellationToken).ConfigureAwait(false);
        }
        await _diagnostics.OnCollectionCompletedAsync(collectionResult, cancellationToken).ConfigureAwait(false);
        await _diagnostics.OnTelemetryAsync(new SnapshotPhaseTelemetry
        {
            PhaseName = "collect",
            Duration = stopwatch.Elapsed,
            ItemsProcessed = collectionResult.Items.Count
        }, cancellationToken).ConfigureAwait(false);

        var itemsToAnalyze = collectionResult.Items
            .Where(i => i.Decision == ProcedureCollectionDecision.Analyze)
            .ToArray();

        stopwatch.Restart();
        var analysisResults = await _analyzer.AnalyzeAsync(itemsToAnalyze, options, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        await _diagnostics.OnAnalysisCompletedAsync(analysisResults.Count, cancellationToken).ConfigureAwait(false);
        await _diagnostics.OnTelemetryAsync(new SnapshotPhaseTelemetry
        {
            PhaseName = "analyze",
            Duration = stopwatch.Elapsed,
            ItemsProcessed = analysisResults.Count
        }, cancellationToken).ConfigureAwait(false);

        stopwatch.Restart();
        var writeResult = await _writer.WriteAsync(analysisResults, options, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var updatedProcedures = writeResult.UpdatedProcedures ?? Array.Empty<ProcedureAnalysisResult>();
        if (updatedProcedures.Count == 0 && analysisResults.Count > 0)
        {
            updatedProcedures = analysisResults;
        }
        foreach (var updated in updatedProcedures)
        {
            await _cache.RecordAnalysisAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        await _cache.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _diagnostics.OnWriteCompletedAsync(writeResult, cancellationToken).ConfigureAwait(false);
        await _diagnostics.OnTelemetryAsync(new SnapshotPhaseTelemetry
        {
            PhaseName = "write",
            Duration = stopwatch.Elapsed,
            ItemsProcessed = writeResult.FilesWritten + writeResult.FilesUnchanged,
            Notes = writeResult.FilesWritten > 0 ? $"written={writeResult.FilesWritten}" : ""
        }, cancellationToken).ConfigureAwait(false);

        return new SnapshotBuildResult
        {
            ProceduresAnalyzed = updatedProcedures.Count,
            ProceduresSkipped = collectionResult.Items.Count(i => i.Decision == ProcedureCollectionDecision.Skip),
            ProceduresReused = reusedItems.Length,
            FilesWritten = writeResult.FilesWritten,
            FilesUnchanged = writeResult.FilesUnchanged,
            ProceduresSelected = selectedDescriptors
        };
    }
}

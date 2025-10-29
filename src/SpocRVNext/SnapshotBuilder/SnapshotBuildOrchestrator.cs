using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

        var totalStopwatch = Stopwatch.StartNew();
        var stopwatch = Stopwatch.StartNew();
        var collectDuration = TimeSpan.Zero;
        var analyzeDuration = TimeSpan.Zero;
        var writeDuration = TimeSpan.Zero;
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
        collectDuration = stopwatch.Elapsed;
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
        analyzeDuration = stopwatch.Elapsed;
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
        totalStopwatch.Stop();
        writeDuration = stopwatch.Elapsed;

        var diagnostics = BuildColumnMetrics(updatedProcedures);

        return new SnapshotBuildResult
        {
            ProceduresAnalyzed = updatedProcedures.Count,
            ProceduresSkipped = collectionResult.Items.Count(i => i.Decision == ProcedureCollectionDecision.Skip),
            ProceduresReused = reusedItems.Length,
            FilesWritten = writeResult.FilesWritten,
            FilesUnchanged = writeResult.FilesUnchanged,
            CollectDuration = collectDuration,
            AnalyzeDuration = analyzeDuration,
            WriteDuration = writeDuration,
            TotalDuration = totalStopwatch.Elapsed,
            ProceduresSelected = selectedDescriptors,
            Diagnostics = diagnostics
        };
    }

    private static IReadOnlyDictionary<string, string>? BuildColumnMetrics(IReadOnlyList<ProcedureAnalysisResult> procedures)
    {
        if (procedures == null || procedures.Count == 0)
        {
            return null;
        }

        int parameterCount = 0;
        int outputParameterCount = 0;
        int tableTypeParameterCount = 0;
        int resultSetCount = 0;
        int jsonResultSetCount = 0;
        int resultColumnsTopLevel = 0;
        int resultColumnsNested = 0;
        int jsonColumnCount = 0;

        foreach (var procedure in procedures)
        {
            if (procedure == null)
            {
                continue;
            }

            var parameters = procedure.Parameters;
            if (parameters != null)
            {
                foreach (var parameter in parameters)
                {
                    if (parameter == null)
                    {
                        continue;
                    }

                    parameterCount++;
                    if (parameter.IsOutput)
                    {
                        outputParameterCount++;
                    }

                    if (parameter.IsTableType)
                    {
                        tableTypeParameterCount++;
                    }
                }
            }

            var model = procedure.Procedure;
            if (model?.ResultSets == null || model.ResultSets.Count == 0)
            {
                continue;
            }

            resultSetCount += model.ResultSets.Count;
            foreach (var resultSet in model.ResultSets)
            {
                if (resultSet == null)
                {
                    continue;
                }

                if (resultSet.ReturnsJson)
                {
                    jsonResultSetCount++;
                }

                var counts = CountColumns(resultSet.Columns);
                resultColumnsTopLevel += counts.Direct;
                resultColumnsNested += counts.Nested;
                jsonColumnCount += counts.Json;
            }
        }

        if (parameterCount == 0 && resultSetCount == 0 && resultColumnsTopLevel == 0 && resultColumnsNested == 0)
        {
            return null;
        }

        var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["parameters.total"] = parameterCount.ToString(CultureInfo.InvariantCulture),
            ["parameters.output"] = outputParameterCount.ToString(CultureInfo.InvariantCulture),
            ["parameters.tableType"] = tableTypeParameterCount.ToString(CultureInfo.InvariantCulture),
            ["resultSets.total"] = resultSetCount.ToString(CultureInfo.InvariantCulture),
            ["resultSets.json"] = jsonResultSetCount.ToString(CultureInfo.InvariantCulture),
            ["resultColumns.topLevel"] = resultColumnsTopLevel.ToString(CultureInfo.InvariantCulture),
            ["resultColumns.nested"] = resultColumnsNested.ToString(CultureInfo.InvariantCulture),
            ["resultColumns.json"] = jsonColumnCount.ToString(CultureInfo.InvariantCulture)
        };

        return metrics;
    }

    private static (int Direct, int Nested, int Json) CountColumns(IReadOnlyList<ProcedureResultColumn> columns)
    {
        if (columns == null || columns.Count == 0)
        {
            return (0, 0, 0);
        }

        int direct = 0;
        int nested = 0;
        int json = 0;

        foreach (var column in columns)
        {
            if (column == null)
            {
                continue;
            }

            direct++;
            if (column.ReturnsJson == true || column.IsNestedJson == true)
            {
                json++;
            }

            if (column.Columns != null && column.Columns.Count > 0)
            {
                var childCounts = CountColumns(column.Columns);
                nested += childCounts.Direct + childCounts.Nested;
                json += childCounts.Json;
            }
        }

        return (direct, nested, json);
    }
}

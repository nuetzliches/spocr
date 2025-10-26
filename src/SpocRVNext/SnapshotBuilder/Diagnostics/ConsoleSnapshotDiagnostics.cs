using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.Services;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.SnapshotBuilder.Diagnostics;

internal sealed class ConsoleSnapshotDiagnostics : ISnapshotDiagnostics
{
    private readonly IConsoleService _console;
    private readonly Dictionary<string, SnapshotPhaseTelemetry> _phaseTelemetry = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private int _collectedCount;
    private int _analyzeSelectedCount;
    private int _reuseCount;
    private int _skipCount;
    private int _analysisCompletedCount;
    private SnapshotWriteResult? _lastWriteResult;

    public ConsoleSnapshotDiagnostics(IConsoleService console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public ValueTask OnCollectionCompletedAsync(ProcedureCollectionResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (result is null || result.Items.Count == 0)
        {
            _console.Info("[snapshot] No procedures discovered during collection.");
            return ValueTask.CompletedTask;
        }

        var total = result.Items.Count;
        var analyze = result.Items.Count(static item => item.Decision == ProcedureCollectionDecision.Analyze);
        var reuse = result.Items.Count(static item => item.Decision == ProcedureCollectionDecision.Reuse);
        var skip = result.Items.Count(static item => item.Decision == ProcedureCollectionDecision.Skip);

        _console.Info($"[snapshot] Collected {total} procedures (analyze={analyze}, reuse={reuse}, skip={skip}).");

        _collectedCount = total;
        _analyzeSelectedCount = analyze;
        _reuseCount = reuse;
        _skipCount = skip;

        if (analyze > 0)
        {
            var samples = result.Items
                .Where(static item => item.Decision == ProcedureCollectionDecision.Analyze)
                .Take(5)
                .Select(static item => $"{item.Descriptor.Schema}.{item.Descriptor.Name}")
                .ToArray();

            if (samples.Length > 0)
            {
                var suffix = analyze > samples.Length ? ", ..." : string.Empty;
                _console.Verbose($"[snapshot] Procedures selected for analysis: {string.Join(", ", samples)}{suffix}");
            }
        }

        if (reuse > 0)
        {
            _console.Verbose($"[snapshot] Reused cached snapshots for {reuse} procedures.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnAnalysisCompletedAsync(int analyzedCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _console.Info($"[snapshot] Analysis completed for {analyzedCount} procedure{(analyzedCount == 1 ? string.Empty : "s")}.");
        _analysisCompletedCount = analyzedCount;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnWriteCompletedAsync(SnapshotWriteResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filesWritten = result?.FilesWritten ?? 0;
        var filesUnchanged = result?.FilesUnchanged ?? 0;

        _console.Info($"[snapshot] Snapshot write finished (written={filesWritten}, unchanged={filesUnchanged}).");

        var updated = result?.UpdatedProcedures ?? Array.Empty<ProcedureAnalysisResult>();
        if (updated.Count > 0)
        {
            var names = updated
                .Select(static proc => $"{proc.Descriptor.Schema}.{proc.Descriptor.Name}")
                .ToArray();
            _console.Verbose($"[snapshot] Updated procedures: {string.Join(", ", names)}");
        }

        _lastWriteResult = result;

        return ValueTask.CompletedTask;
    }

    public ValueTask OnTelemetryAsync(SnapshotPhaseTelemetry telemetry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (telemetry is null || string.IsNullOrEmpty(telemetry.PhaseName))
        {
            return ValueTask.CompletedTask;
        }

        lock (_sync)
        {
            _phaseTelemetry[telemetry.PhaseName] = telemetry;
        }

        var durationMs = telemetry.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
        var notes = string.IsNullOrWhiteSpace(telemetry.Notes) ? string.Empty : $" notes={telemetry.Notes}";

        _console.Verbose($"[snapshot] phase={telemetry.PhaseName} duration={durationMs}ms items={telemetry.ItemsProcessed}{notes}");

        if (string.Equals(telemetry.PhaseName, "write", StringComparison.OrdinalIgnoreCase))
        {
            var phaseSummary = BuildPhaseSummary();
            if (!string.IsNullOrEmpty(phaseSummary))
            {
                _console.Info($"[snapshot] timing: {phaseSummary}");
            }

            PersistSummaryIfRequested(_lastWriteResult);
        }

        return ValueTask.CompletedTask;
    }

    private string BuildPhaseSummary()
    {
        SnapshotPhaseTelemetry? collect;
        SnapshotPhaseTelemetry? analyze;
        SnapshotPhaseTelemetry? write;
        SnapshotPhaseTelemetry[] additional;

        lock (_sync)
        {
            _phaseTelemetry.TryGetValue("collect", out collect);
            _phaseTelemetry.TryGetValue("analyze", out analyze);
            _phaseTelemetry.TryGetValue("write", out write);
            additional = _phaseTelemetry
                .Where(static pair => pair.Key is not "collect" and not "analyze" and not "write")
                .Select(static pair => pair.Value)
                .Where(static t => t is not null)
                .ToArray();
        }

        var segments = new List<string>();
        if (collect != null)
        {
            segments.Add(FormatTelemetry(collect));
        }
        if (analyze != null)
        {
            segments.Add(FormatTelemetry(analyze));
        }
        if (write != null)
        {
            segments.Add(FormatTelemetry(write));
        }
        foreach (var telemetry in additional)
        {
            segments.Add(FormatTelemetry(telemetry));
        }

        return segments.Count == 0 ? string.Empty : string.Join(", ", segments);
    }

    private static string FormatTelemetry(SnapshotPhaseTelemetry telemetry)
    {
        var duration = telemetry.Duration.TotalMilliseconds;
        var itemsTag = telemetry.ItemsProcessed > 0 ? $" items={telemetry.ItemsProcessed}" : string.Empty;
        return $"{telemetry.PhaseName}={duration:F0}ms{itemsTag}";
    }

    private void PersistSummaryIfRequested(SnapshotWriteResult? writeResult)
    {
        var explicitPath = Environment.GetEnvironmentVariable("SPOCR_SNAPSHOT_SUMMARY_PATH");
        var summaryFlag = Environment.GetEnvironmentVariable("SPOCR_SNAPSHOT_SUMMARY");
        var summaryValue = summaryFlag?.Trim();
        var summaryEnabled = !string.IsNullOrWhiteSpace(summaryValue) && !string.Equals(summaryValue, "0", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(explicitPath) && !summaryEnabled)
        {
            return;
        }

        string path;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            path = explicitPath!;
        }
        else
        {
            var projectRoot = ProjectRootResolver.GetSolutionRootOrCwd();
            path = Path.Combine(projectRoot, "debug", "snapshot-summary.json");
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            Dictionary<string, SnapshotPhaseTelemetry> phases;
            lock (_sync)
            {
                phases = new Dictionary<string, SnapshotPhaseTelemetry>(_phaseTelemetry, StringComparer.OrdinalIgnoreCase);
            }

            var phasePayload = phases.ToDictionary(
                static pair => pair.Key,
                static pair => new
                {
                    durationMs = Math.Round(pair.Value.Duration.TotalMilliseconds, 2, MidpointRounding.AwayFromZero),
                    items = pair.Value.ItemsProcessed,
                    notes = pair.Value.Notes
                },
                StringComparer.OrdinalIgnoreCase);

            var payload = new
            {
                timestampUtc = DateTime.UtcNow,
                procedures = new
                {
                    collected = _collectedCount,
                    analyzeSelected = _analyzeSelectedCount,
                    analysisCompleted = _analysisCompletedCount,
                    reuse = _reuseCount,
                    skip = _skipCount
                },
                files = new
                {
                    written = writeResult?.FilesWritten ?? 0,
                    unchanged = writeResult?.FilesUnchanged ?? 0
                },
                phases = phasePayload,
                updatedCount = writeResult?.UpdatedProcedures?.Count ?? 0
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(payload, options);
            File.WriteAllText(path, json);
            _console.Verbose($"[snapshot] Summary persisted to {path}");
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot] Failed to persist summary: {ex.Message}");
        }
    }
}

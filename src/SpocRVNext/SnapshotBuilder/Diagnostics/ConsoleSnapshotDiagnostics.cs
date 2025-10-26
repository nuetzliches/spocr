using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.Services;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Diagnostics;

internal sealed class ConsoleSnapshotDiagnostics : ISnapshotDiagnostics
{
    private readonly IConsoleService _console;
    private readonly Dictionary<string, SnapshotPhaseTelemetry> _phaseTelemetry = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

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
}

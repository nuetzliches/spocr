using System;

namespace SpocR.SpocRVNext.SnapshotBuilder.Diagnostics;

public sealed class SnapshotPhaseTelemetry
{
    public string PhaseName { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
    public int ItemsProcessed { get; init; }
    public string? Notes { get; init; }
}

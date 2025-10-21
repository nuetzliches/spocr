namespace SpocR.Telemetry;

/// <summary>
/// Abstraction for recording experimental CLI usage events.
/// </summary>
public interface IExperimentalCliTelemetry
{
    void Record(ExperimentalCliUsageEvent evt);
}

/// <summary>
/// Simple event data structure.
/// </summary>
public sealed record ExperimentalCliUsageEvent(string command, string mode, System.TimeSpan duration, bool success);

/// <summary>
/// Console-based implementation (placeholder for future structured logging / OTLP export).
/// </summary>
public sealed class ConsoleExperimentalCliTelemetry : IExperimentalCliTelemetry
{
    public void Record(ExperimentalCliUsageEvent evt)
    {
        // Only emit telemetry line when verbose mode enabled to reduce default console noise.
        if (string.Equals(System.Environment.GetEnvironmentVariable("SPOCR_VERBOSE"), "1", System.StringComparison.Ordinal))
        {
            System.Console.WriteLine($"[telemetry experimental-cli] command={evt.command} mode={evt.mode} success={evt.success} durationMs={evt.duration.TotalMilliseconds:F0}");
        }
    }
}

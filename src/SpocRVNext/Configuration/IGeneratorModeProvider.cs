using System;

namespace SpocRVNext.Configuration;

/// <summary>
/// Provides the current generator mode (legacy | dual | next) to vNext generators.
/// Abstracted for deterministic unit testing without relying on ambient environment variables.
/// </summary>
public interface IGeneratorModeProvider
{
    string Mode { get; }
}

/// <summary>
/// Default implementation reading the process environment variable SPOCR_GENERATOR_MODE (default dual).
/// </summary>
public sealed class EnvGeneratorModeProvider : IGeneratorModeProvider
{
    public string Mode
    {
        get
        {
            var mode = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE")?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(mode)) mode = "dual"; // default bridge behavior
            return mode!;
        }
    }
}
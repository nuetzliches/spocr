using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#nullable enable

namespace SpocRVNext.Configuration;

/// <summary>
/// Represents strongly typed configuration values for the vNext generator.
/// Values are resolved using precedence: CLI overrides (passed in externally) > process environment variables > .env file values > legacy spocr.json mapping (future fallback; not yet implemented here).
/// Legacy spocr.json fallback will be removed in v5.0.
/// </summary>
public sealed class EnvConfiguration
{
    public string GeneratorMode { get; init; } = "dual"; // legacy | dual | next
    public string? DefaultConnection { get; init; }
    public string? NamespaceRoot { get; init; }

    public static EnvConfiguration Load(string? projectRoot = null, IDictionary<string, string?>? cliOverrides = null)
    {
        projectRoot ??= Directory.GetCurrentDirectory();
        var envFilePath = ResolveEnvFile(projectRoot);
        var filePairs = LoadDotEnv(envFilePath);

        string Get(string key)
        {
            // CLI override
            if (cliOverrides != null && cliOverrides.TryGetValue(key, out var fromCli) && !string.IsNullOrWhiteSpace(fromCli))
                return fromCli!;
            // Environment variable
            var fromProcess = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(fromProcess))
                return fromProcess!;
            // .env file
            if (filePairs.TryGetValue(key, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile))
                return fromFile!;
            return string.Empty;
        }

        var cfg = new EnvConfiguration
        {
            GeneratorMode = NormalizeMode(Get("SPOCR_GENERATOR_MODE")),
            DefaultConnection = NullIfEmpty(Get("SPOCR_DB_DEFAULT")),
            NamespaceRoot = NullIfEmpty(Get("SPOCR_NAMESPACE"))
        };

        Validate(cfg, envFilePath);
        return cfg;
    }

    private static string NormalizeMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "dual"; // default
        mode = mode.Trim().ToLowerInvariant();
        return mode switch
        {
            "legacy" or "dual" or "next" => mode,
            _ => throw new InvalidOperationException($"Unsupported SPOCR_GENERATOR_MODE='{mode}'. Allowed: legacy | dual | next")
        };
    }

    private static void Validate(EnvConfiguration cfg, string? envFilePath)
    {
        // Requirement: at least one source must exist (.env or environment) for dual/next modes for NamespaceRoot (soft warning for now)
        if (cfg.GeneratorMode is "dual" or "next")
        {
            if (string.IsNullOrWhiteSpace(cfg.NamespaceRoot))
            {
                // For now do not throw; future versions might enforce.
                Console.Error.WriteLine("[spocr vNext] Warning: SPOCR_NAMESPACE not set. Falling back to legacy namespace resolution if available.");
            }
        }

        if (!string.IsNullOrEmpty(envFilePath) && !File.Exists(envFilePath))
        {
            // Accept absence silently; config may be provided through environment or CLI.
        }
    }

    private static string? ResolveEnvFile(string projectRoot)
    {
        // Check root (legacy position removed) then sample project if exists
        var candidatePaths = new List<string>
        {
            Path.Combine(projectRoot, ".env"),
            Path.Combine(projectRoot, ".env.local"),
            // sample path (for feature branch usage) - walk upward to find samples/restapi/.env or .env.example if copying later
            Path.Combine(projectRoot, "samples", "restapi", ".env"),
            Path.Combine(projectRoot, "samples", "restapi", ".env.local")
        };
        return candidatePaths.FirstOrDefault(File.Exists) ?? candidatePaths.First();
    }

    private static Dictionary<string, string?> LoadDotEnv(string? path)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (path == null || !File.Exists(path)) return dict;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line.Substring(0, idx).Trim();
            var value = line.Substring(idx + 1).Trim();
            // Remove optional surrounding quotes
            if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value.Substring(1, value.Length - 2);
            }
            dict[key] = value;
        }
        return dict;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

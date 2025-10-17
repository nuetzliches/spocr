using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpocRVNext.Configuration;

/// <summary>
/// Strongly typed configuration for vNext generator with precedence:
/// CLI overrides > Environment Variables > .env file > (bridge) spocr.json fallback for connection
/// </summary>
public sealed class EnvConfiguration
{
    public string GeneratorMode { get; init; } = "dual"; // legacy | dual | next
    public string? GeneratorConnectionString { get; init; }
    public string? DefaultConnection { get; init; }
    public string? NamespaceRoot { get; init; }
    public string? OutputDir { get; init; }
    public string? ConfigPath { get; init; }
    /// <summary>
    /// Positive allow-list for schemas to generate (SPOCR_BUILD_SCHEMAS). Empty => fallback to ignored-schemas exclusion.
    /// </summary>
    public IReadOnlyList<string> BuildSchemas { get; init; } = Array.Empty<string>();

    public static EnvConfiguration Load(string? projectRoot = null, IDictionary<string, string?>? cliOverrides = null, string? explicitConfigPath = null)
    {
        projectRoot ??= Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(explicitConfigPath) && File.Exists(explicitConfigPath))
        {
            try
            {
                var cfgDirOverride = Path.GetDirectoryName(Path.GetFullPath(explicitConfigPath));
                if (!string.IsNullOrWhiteSpace(cfgDirOverride)) projectRoot = cfgDirOverride!;
            }
            catch { }
        }
        else
        {
            try
            {
                var localCfg = Directory.EnumerateFiles(projectRoot, "spocr.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (localCfg != null)
                {
                    var cfgDir = Path.GetDirectoryName(Path.GetFullPath(localCfg));
                    if (!string.IsNullOrWhiteSpace(cfgDir)) projectRoot = cfgDir!;
                }
            }
            catch { }
        }

        var envFilePath = ResolveEnvFile(projectRoot);
        var filePairs = LoadDotEnv(envFilePath);

        string Get(string key)
        {
            if (cliOverrides != null && cliOverrides.TryGetValue(key, out var fromCli) && !string.IsNullOrWhiteSpace(fromCli)) return fromCli!;
            var fromProcess = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(fromProcess)) return fromProcess!;
            if (filePairs.TryGetValue(key, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile)) return fromFile!;
            return string.Empty;
        }

        var fullConn = NullIfEmpty(Get("SPOCR_GENERATOR_DB"));
        var modeResolved = NormalizeMode(Get("SPOCR_GENERATOR_MODE"));
        var buildSchemasList = ParseList(NullIfEmpty(Get("SPOCR_BUILD_SCHEMAS")));

        if (modeResolved is "dual" or "next" && string.IsNullOrWhiteSpace(fullConn))
        {
            try
            {
                var nearestConfig = FindNearestConfig(projectRoot);
                if (nearestConfig != null)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(nearestConfig));
                    var root = doc.RootElement;
                    string? conn = root.TryGetProperty("Project", out var p) && p.TryGetProperty("DataBase", out var db) && db.TryGetProperty("ConnectionString", out var csEl) ? csEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(conn))
                    {
                        fullConn = conn;
                        Console.Out.WriteLine("[spocr vNext] Info: SPOCR_GENERATOR_DB not set – using spocr.json connection (bridge). Consider adding SPOCR_GENERATOR_DB to .env.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[spocr vNext] Warning: spocr.json connection fallback failed: {ex.Message}");
            }
        }
        else if (modeResolved is "dual" or "next" && !string.IsNullOrWhiteSpace(fullConn))
        {
            Console.Out.WriteLine("[spocr vNext] Info: Connection from SPOCR_GENERATOR_DB resolved. spocr.json ignored.");
        }

        var cfg = new EnvConfiguration
        {
            GeneratorMode = modeResolved,
            GeneratorConnectionString = fullConn,
            DefaultConnection = fullConn,
            NamespaceRoot = NullIfEmpty(Get("SPOCR_NAMESPACE")),
            OutputDir = NullIfEmpty(Get("SPOCR_OUTPUT_DIR")),
            ConfigPath = explicitConfigPath,
            BuildSchemas = buildSchemasList
        };

        if (string.IsNullOrWhiteSpace(cfg.OutputDir))
        {
            cfg = new EnvConfiguration
            {
                GeneratorMode = cfg.GeneratorMode,
                GeneratorConnectionString = cfg.GeneratorConnectionString,
                DefaultConnection = cfg.DefaultConnection,
                NamespaceRoot = cfg.NamespaceRoot,
                OutputDir = "SpocR",
                ConfigPath = cfg.ConfigPath,
                BuildSchemas = cfg.BuildSchemas
            };
        }

        if (cfg.GeneratorMode is "dual" or "next")
        {
            if (string.IsNullOrEmpty(envFilePath) || !File.Exists(envFilePath))
            {
                try
                {
                    Console.WriteLine("[spocr vNext] Migration: no .env file found.");
                    Console.Write("[spocr vNext] Create new .env now? (Y/n): ");
                    string? answer = null; try { answer = Console.ReadLine(); } catch { }
                    bool create = true;
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        var a = answer.Trim();
                        if (a.Equals("n", StringComparison.OrdinalIgnoreCase) || a.Equals("no", StringComparison.OrdinalIgnoreCase)) create = false;
                    }
                    if (!create)
                    {
                        Console.WriteLine("[spocr vNext] Skipped .env creation -> switching to legacy mode.");
                        try { Environment.SetEnvironmentVariable("SPOCR_GENERATOR_MODE", "legacy"); } catch { }
                        cfg = new EnvConfiguration
                        {
                            GeneratorMode = "legacy",
                            GeneratorConnectionString = cfg.GeneratorConnectionString,
                            DefaultConnection = cfg.DefaultConnection,
                            NamespaceRoot = cfg.NamespaceRoot,
                            OutputDir = cfg.OutputDir,
                            ConfigPath = cfg.ConfigPath,
                            BuildSchemas = cfg.BuildSchemas
                        };
                        Validate(cfg, envFilePath);
                        return cfg;
                    }
                }
                catch { }

                var disableBootstrap = Environment.GetEnvironmentVariable("SPOCR_DISABLE_ENV_BOOTSTRAP");
                if (!string.IsNullOrWhiteSpace(disableBootstrap) && disableBootstrap != "0")
                    throw new InvalidOperationException(".env bootstrap disabled via SPOCR_DISABLE_ENV_BOOTSTRAP; required in dual/next mode.");

                try
                {
                    var autoPrefill = TryPrefillFromConfig(projectRoot);
                    if (autoPrefill != null)
                    {
                        envFilePath = autoPrefill;
                        filePairs = LoadDotEnv(envFilePath);
                    }
                }
                catch (Exception px)
                {
                    Console.Error.WriteLine($"[spocr vNext] Prefill skipped: {px.Message}");
                }
                try
                {
                    var bootstrapPath = SpocR.SpocRVNext.Cli.EnvBootstrapper.EnsureEnvAsync(projectRoot, cfg.GeneratorMode).GetAwaiter().GetResult();
                    if (!File.Exists(bootstrapPath) && cfg.GeneratorMode != "legacy")
                    {
                        cfg = new EnvConfiguration
                        {
                            GeneratorMode = "legacy",
                            GeneratorConnectionString = cfg.GeneratorConnectionString,
                            DefaultConnection = cfg.DefaultConnection,
                            NamespaceRoot = cfg.NamespaceRoot,
                            OutputDir = cfg.OutputDir,
                            ConfigPath = cfg.ConfigPath,
                            BuildSchemas = cfg.BuildSchemas
                        };
                    }
                    else if (File.Exists(bootstrapPath))
                    {
                        envFilePath = bootstrapPath;
                        filePairs = LoadDotEnv(envFilePath);
                    }
                }
                catch
                {
                    cfg = new EnvConfiguration
                    {
                        GeneratorMode = "legacy",
                        GeneratorConnectionString = cfg.GeneratorConnectionString,
                        DefaultConnection = cfg.DefaultConnection,
                        NamespaceRoot = cfg.NamespaceRoot,
                        OutputDir = cfg.OutputDir,
                        ConfigPath = cfg.ConfigPath,
                        BuildSchemas = cfg.BuildSchemas
                    };
                }
            }
        }

        Validate(cfg, envFilePath);
        return cfg;
    }

    private static string NormalizeMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "dual";
        mode = mode.Trim().ToLowerInvariant();
        return mode switch
        {
            "legacy" or "dual" or "next" => mode,
            _ => throw new InvalidOperationException($"Unsupported SPOCR_GENERATOR_MODE='{mode}'. Allowed: legacy | dual | next")
        };
    }

    private static void Validate(EnvConfiguration cfg, string? envFilePath)
    {
        if (!string.IsNullOrWhiteSpace(cfg.NamespaceRoot))
        {
            var ns = cfg.NamespaceRoot.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(ns, @"^[A-Za-z_][A-Za-z0-9_\.]*$"))
                throw new InvalidOperationException($"SPOCR_NAMESPACE '{ns}' invalid.");
            if (ns.Contains(".."))
                throw new InvalidOperationException("SPOCR_NAMESPACE contains '..'.");
        }
        if (!string.IsNullOrWhiteSpace(cfg.OutputDir) && cfg.OutputDir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException($"SPOCR_OUTPUT_DIR '{cfg.OutputDir}' contains invalid chars.");
        if (cfg.GeneratorMode is "dual" or "next")
        {
            if (string.IsNullOrEmpty(envFilePath) || !File.Exists(envFilePath))
                throw new InvalidOperationException("In dual/next a .env file must exist.");
            var hasMarker = File.ReadLines(envFilePath).Any(l => l.Contains("SPOCR_", StringComparison.OrdinalIgnoreCase));
            if (!hasMarker)
                throw new InvalidOperationException(".env file has no SPOCR_ marker lines.");
        }
        foreach (var schema in cfg.BuildSchemas)
        {
            var s = schema.Trim(); if (s.Length == 0) continue;
            // Allow hyphen-separated schema names (e.g. workflow-state) – sanitized to PascalCase via NamePolicy.Sanitize.
            if (!System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z_][A-Za-z0-9_-]*$"))
                throw new InvalidOperationException($"SPOCR_BUILD_SCHEMAS entry '{s}' invalid (pattern ^[A-Za-z_][A-Za-z0-9_-]*$).");
        }
    }

    private static string? ResolveEnvFile(string projectRoot)
    {
        var primary = Path.Combine(projectRoot, ".env");
        if (File.Exists(primary)) return primary;
        var local = Path.Combine(projectRoot, ".env.local");
        if (File.Exists(local)) return local;
        return primary;
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
            if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
                value = value.Substring(1, value.Length - 2);
            dict[key] = value;
        }
        return dict;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(p => p.Trim())
                  .Where(p => p.Length > 0)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    private static string? TryPrefillFromConfig(string projectRoot)
    {
        string? targetConfig = null;
        if (File.Exists(Path.Combine(projectRoot, "spocr.json"))) targetConfig = Path.Combine(projectRoot, "spocr.json");
        else
        {
            try { targetConfig = Directory.EnumerateFiles(projectRoot, "spocr.json", SearchOption.AllDirectories).FirstOrDefault(); } catch { }
        }
        if (targetConfig == null) return null;
        var cfgDir = Path.GetDirectoryName(targetConfig)!;
        var envPath = Path.Combine(cfgDir, ".env");
        if (File.Exists(envPath)) return envPath;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(targetConfig));
            var root = doc.RootElement;
            string? ns = root.TryGetProperty("Project", out var p) && p.TryGetProperty("Output", out var o) && o.TryGetProperty("Namespace", out var nsEl) ? nsEl.GetString() : null;
            string? tfm = root.TryGetProperty("TargetFramework", out var tfmEl) ? tfmEl.GetString() : null;
            string? conn = root.TryGetProperty("Project", out p) && p.TryGetProperty("DataBase", out var db) && db.TryGetProperty("ConnectionString", out var cs) ? cs.GetString() : null;
            var lines = new List<string>
            {
                "# Auto-prefilled by SpocR vNext",
                "SPOCR_GENERATOR_MODE=dual",
                ns is not null ? $"SPOCR_NAMESPACE={ns}" : "# SPOCR_NAMESPACE=Your.Project.Namespace",
                "SPOCR_OUTPUT_DIR=SpocR",
                tfm is not null ? $"SPOCR_TFM={tfm}" : "# SPOCR_TFM=net9.0",
                conn is not null ? $"SPOCR_GENERATOR_DB={conn}" : "# SPOCR_GENERATOR_DB=FullConnectionStringHere"
            };
            File.WriteAllLines(envPath, lines);
            return envPath;
        }
        catch { return null; }
    }

    private static string? FindNearestConfig(string projectRoot)
    {
        if (File.Exists(Path.Combine(projectRoot, "spocr.json"))) return Path.Combine(projectRoot, "spocr.json");
        try { return Directory.EnumerateFiles(projectRoot, "spocr.json", SearchOption.AllDirectories).FirstOrDefault(); }
        catch { return null; }
    }
}

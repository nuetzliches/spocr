using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpocRVNext.Configuration;

/// <summary>
/// Represents strongly typed configuration values for the vNext generator.
/// Values are resolved using precedence: CLI overrides (passed in externally) > process environment variables > .env file values > legacy spocr.json mapping (future fallback; not yet implemented here).
/// Legacy spocr.json fallback will be removed in v5.0.
/// </summary>
public sealed class EnvConfiguration
{
    public string GeneratorMode { get; init; } = "dual"; // legacy | dual | next
    public string? GeneratorConnectionString { get; init; } // from SPOCR_GENERATOR_DB (full connection string)
    public string? DefaultConnection { get; init; } // backward compatibility mirror (legacy identifier concept removed)
    public string? NamespaceRoot { get; init; } // from SPOCR_NAMESPACE
    public string? OutputDir { get; init; }
    // Pfad zur verwendeten spocr.json (falls via CLI -p übergeben), wird extern gesetzt
    public string? ConfigPath { get; init; }

    /// <summary>
    /// Load configuration. projectRoot should be the execution / target project directory (path passed via --path in legacy CLI) so that .env discovery & creation occur there.
    /// </summary>
    public static EnvConfiguration Load(string? projectRoot = null, IDictionary<string, string?>? cliOverrides = null, string? explicitConfigPath = null)
    {
        projectRoot ??= Directory.GetCurrentDirectory();
        // Determine config directory: explicit > nearest spocr.json in current working directory scope
        if (!string.IsNullOrWhiteSpace(explicitConfigPath) && File.Exists(explicitConfigPath))
        {
            try
            {
                var cfgDirOverride = Path.GetDirectoryName(Path.GetFullPath(explicitConfigPath));
                if (!string.IsNullOrWhiteSpace(cfgDirOverride)) projectRoot = cfgDirOverride!;
            }
            catch { /* ignore path issues */ }
        }
        else
        {
            // Auto-detect spocr.json in current working directory (no recursion beyond one directory depth to keep semantics simple)
            try
            {
                var localCfg = Directory.EnumerateFiles(projectRoot, "spocr.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (localCfg != null)
                {
                    var cfgDir = Path.GetDirectoryName(Path.GetFullPath(localCfg));
                    if (!string.IsNullOrWhiteSpace(cfgDir)) projectRoot = cfgDir!;
                }
            }
            catch { /* ignore */ }
        }
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

        // New variable names (aliases kept):
        var fullConn = NullIfEmpty(Get("SPOCR_GENERATOR_DB"));
        var cfg = new EnvConfiguration
        {
            GeneratorMode = NormalizeMode(Get("SPOCR_GENERATOR_MODE")),
            GeneratorConnectionString = fullConn,
            DefaultConnection = fullConn,
            NamespaceRoot = NullIfEmpty(Get("SPOCR_NAMESPACE")),
            OutputDir = NullIfEmpty(Get("SPOCR_OUTPUT_DIR")),
            ConfigPath = explicitConfigPath
        };

        // Apply default for OutputDir
        if (string.IsNullOrWhiteSpace(cfg.OutputDir))
        {
            cfg = new EnvConfiguration
            {
                GeneratorMode = cfg.GeneratorMode,
                GeneratorConnectionString = cfg.GeneratorConnectionString,
                DefaultConnection = cfg.DefaultConnection,
                NamespaceRoot = cfg.NamespaceRoot,
                OutputDir = "SpocR",
                ConfigPath = cfg.ConfigPath
            };
        }

        // If dual/next and .env missing, interactive migration bootstrap (console scenario only)
        if (cfg.GeneratorMode is "dual" or "next")
        {
            if (string.IsNullOrEmpty(envFilePath) || !File.Exists(envFilePath))
            {
                // Always prompt in dual/next when the .env file is missing (first-run migration experience)
                try
                {
                    Console.WriteLine("[spocr vNext] Migration detected: no .env file present.");
                    Console.WriteLine("[spocr vNext] A .env file is now the primary configuration for the generator (bridge phase v4.5 → v5).");
                    Console.Write("[spocr vNext] Create a new .env now? (Y/n): ");
                    string? answer = null;
                    try { answer = Console.ReadLine(); } catch { /* non-interactive */ }
                    bool create = true; // default Yes
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        var a = answer.Trim();
                        if (a.Equals("n", StringComparison.OrdinalIgnoreCase) || a.Equals("no", StringComparison.OrdinalIgnoreCase))
                            create = false;
                    }
                    if (!create)
                    {
                        Console.WriteLine("[spocr vNext] Skipped .env creation (user chose 'n'). Falling back to legacy mode for this run.");
                        // Force legacy mode globally for the remaining process so that any mode providers
                        // instantiated earlier but reading environment variables lazily will observe the change.
                        try { Environment.SetEnvironmentVariable("SPOCR_GENERATOR_MODE", "legacy"); } catch { /* ignore */ }
                        cfg = new EnvConfiguration
                        {
                            GeneratorMode = "legacy",
                            GeneratorConnectionString = cfg.GeneratorConnectionString,
                            DefaultConnection = cfg.DefaultConnection,
                            NamespaceRoot = cfg.NamespaceRoot,
                            OutputDir = cfg.OutputDir,
                            ConfigPath = cfg.ConfigPath
                        };
                        Validate(cfg, envFilePath);
                        return cfg;
                    }
                }
                catch { /* ignore interactive issues */ }
                var disableBootstrap = Environment.GetEnvironmentVariable("SPOCR_DISABLE_ENV_BOOTSTRAP");
                if (!string.IsNullOrWhiteSpace(disableBootstrap) && disableBootstrap != "0")
                {
                    // Enforce failure for tests / controlled scenarios before any attempt to prefill or create
                    throw new InvalidOperationException(".env bootstrap disabled via SPOCR_DISABLE_ENV_BOOTSTRAP; .env file is required in dual/next mode.");
                }
                // Attempt automatic prefill from nearest spocr.json before interaction
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
                    Console.Error.WriteLine($"[spocr vNext] prefill skipped: {px.Message}");
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
                            ConfigPath = cfg.ConfigPath
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
                        ConfigPath = cfg.ConfigPath
                    };
                }
            }
        }

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
        // Namespace must be present (already enforced earlier) and follow pattern (letters/underscore start, then letters/digits/._)
        if (!string.IsNullOrWhiteSpace(cfg.NamespaceRoot))
        {
            var ns = cfg.NamespaceRoot.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(ns, @"^[A-Za-z_][A-Za-z0-9_\.]*$"))
            {
                throw new InvalidOperationException($"SPOCR_NAMESPACE '{ns}' is invalid. Allowed pattern: ^[A-Za-z_][A-Za-z0-9_\\.]*$");
            }
            if (ns.Contains(".."))
            {
                throw new InvalidOperationException("SPOCR_NAMESPACE contains consecutive dots '..' which is not allowed.");
            }
        }
        if (!string.IsNullOrWhiteSpace(cfg.OutputDir))
        {
            if (cfg.OutputDir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException($"SPOCR_OUTPUT_DIR '{cfg.OutputDir}' contains invalid path characters.");
            }
        }
        if (cfg.GeneratorMode is "dual" or "next")
        {
            if (string.IsNullOrEmpty(envFilePath) || !File.Exists(envFilePath))
            {
                throw new InvalidOperationException("In modes 'dual' or 'next' a .env file must exist containing at least one SPOCR_ marker line.");
            }
            // Accept any occurrence of "SPOCR_" (even commented) to allow placeholder only .env
            var hasMarker = File.ReadLines(envFilePath).Any(l => l.Contains("SPOCR_", StringComparison.OrdinalIgnoreCase));
            if (!hasMarker)
            {
                throw new InvalidOperationException(".env file found but contains no SPOCR_ marker lines; add at least a commented SPOCR_ entry (e.g. '# SPOCR_NAMESPACE=Your.Namespace').");
            }
        }
    }

    private static string? ResolveEnvFile(string projectRoot)
    {
        // Simplified: .env must reside next to the active spocr.json (or intended root). No deep searching.
        var primary = Path.Combine(projectRoot, ".env");
        if (File.Exists(primary)) return primary;
        var local = Path.Combine(projectRoot, ".env.local");
        if (File.Exists(local)) return local;
        return primary; // creation target
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

    // Removed previous DeriveNamespace & ToPascalCase (now handled by NamespaceResolver)

    private static string? TryPrefillFromConfig(string projectRoot)
    {
        // Find nearest spocr.json (current dir or any child)
        string? targetConfig = null;
        if (File.Exists(Path.Combine(projectRoot, "spocr.json")))
            targetConfig = Path.Combine(projectRoot, "spocr.json");
        else
        {
            try
            {
                targetConfig = Directory.EnumerateFiles(projectRoot, "spocr.json", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch { }
        }
        if (targetConfig == null) return null;
        var cfgDir = Path.GetDirectoryName(targetConfig)!;
        var envPath = Path.Combine(cfgDir, ".env");
        if (File.Exists(envPath)) return envPath; // already exists
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(targetConfig));
            var root = doc.RootElement;
            string? ns = root.TryGetProperty("Project", out var p) && p.TryGetProperty("Output", out var o) && o.TryGetProperty("Namespace", out var nsEl) ? nsEl.GetString() : null;
            string? tfm = root.TryGetProperty("TargetFramework", out var tfmEl) ? tfmEl.GetString() : null;
            string? conn = root.TryGetProperty("Project", out p) && p.TryGetProperty("DataBase", out var db) && db.TryGetProperty("ConnectionString", out var cs) ? cs.GetString() : null;
            string? id = root.TryGetProperty("Project", out p) && p.TryGetProperty("DataBase", out db) && db.TryGetProperty("RuntimeConnectionStringIdentifier", out var idEl) ? idEl.GetString() : null;
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
        catch
        {
            return null;
        }
    }
}

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
    // Legacy: DefaultConnection captured from SPOCR_DB_DEFAULT (was ambiguous). New explicit fields:
    public string? ConnectionStringIdentifier { get; init; } // from SPOCR_DB_IDENTIFIER or SPOCR_DB_DEFAULT (alias)
    public string? GeneratorConnectionString { get; init; } // from SPOCR_GENERATOR_DB (full connection string)
    public string? DefaultConnection { get; init; } // backward compatibility mirror (will carry GeneratorConnectionString if present otherwise identifier content)
    public string? NamespaceRoot { get; init; }
    public string? OutputDir { get; init; }
    // Pfad zur verwendeten spocr.json (falls via CLI -p übergeben), wird extern gesetzt
    public string? ConfigPath { get; init; }

    /// <summary>
    /// Load configuration. projectRoot should be the execution / target project directory (path passed via --path in legacy CLI) so that .env discovery & creation occur there.
    /// </summary>
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

        // New variable names (aliases kept):
        var id = NullIfEmpty(Get("SPOCR_DB_IDENTIFIER"));
        var legacyId = NullIfEmpty(Get("SPOCR_DB_DEFAULT"));
        if (string.IsNullOrWhiteSpace(id)) id = legacyId; // alias fallback
        var fullConn = NullIfEmpty(Get("SPOCR_GENERATOR_DB"));
        var cfg = new EnvConfiguration
        {
            GeneratorMode = NormalizeMode(Get("SPOCR_GENERATOR_MODE")),
            ConnectionStringIdentifier = id,
            GeneratorConnectionString = fullConn,
            DefaultConnection = fullConn ?? id, // maintain earlier property semantics
            NamespaceRoot = NullIfEmpty(Get("SPOCR_NAMESPACE")),
            OutputDir = NullIfEmpty(Get("SPOCR_OUTPUT_DIR"))
        };

        // Apply default for OutputDir
        if (string.IsNullOrWhiteSpace(cfg.OutputDir))
        {
            cfg = new EnvConfiguration
            {
                GeneratorMode = cfg.GeneratorMode,
                ConnectionStringIdentifier = cfg.ConnectionStringIdentifier,
                GeneratorConnectionString = cfg.GeneratorConnectionString,
                DefaultConnection = cfg.DefaultConnection,
                NamespaceRoot = cfg.NamespaceRoot,
                OutputDir = "SpocR"
            };
        }

        // Fallback: derive namespace if not provided
        if (string.IsNullOrWhiteSpace(cfg.NamespaceRoot))
        {
            var resolver = new NamespaceResolver(cfg, msg => Console.Error.WriteLine(msg));
            var resolved = resolver.Resolve(projectRoot);
            cfg = new EnvConfiguration
            {
                GeneratorMode = cfg.GeneratorMode,
                ConnectionStringIdentifier = cfg.ConnectionStringIdentifier,
                GeneratorConnectionString = cfg.GeneratorConnectionString,
                DefaultConnection = cfg.DefaultConnection,
                NamespaceRoot = resolved,
                OutputDir = cfg.OutputDir
            };
            Console.Out.WriteLine($"[spocr vNext] Info: Namespace for generated files: '{cfg.NamespaceRoot}' (auto-derived; set SPOCR_NAMESPACE to override)");
        }

        // If dual/next and .env missing, interactive bootstrap (console scenario only)
        if (cfg.GeneratorMode is "dual" or "next")
        {
            if (string.IsNullOrEmpty(envFilePath) || !File.Exists(envFilePath))
            {
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
                            ConnectionStringIdentifier = cfg.ConnectionStringIdentifier,
                            GeneratorConnectionString = cfg.GeneratorConnectionString,
                            DefaultConnection = cfg.DefaultConnection,
                            NamespaceRoot = cfg.NamespaceRoot,
                            OutputDir = cfg.OutputDir
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
                        ConnectionStringIdentifier = cfg.ConnectionStringIdentifier,
                        GeneratorConnectionString = cfg.GeneratorConnectionString,
                        DefaultConnection = cfg.DefaultConnection,
                        NamespaceRoot = cfg.NamespaceRoot,
                        OutputDir = cfg.OutputDir
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
        // Prefer .env co-located with a spocr.json (sample) over root if multiple exist.
        var primary = Path.Combine(projectRoot, ".env");
        var local = Path.Combine(projectRoot, ".env.local");
        if (File.Exists(primary)) return primary;
        if (File.Exists(local)) return local;
        // Search for nested spocr.json and accompanying .env
        try
        {
            foreach (var cfgFile in Directory.EnumerateFiles(projectRoot, "spocr.json", SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(cfgFile)!;
                var nestedEnv = Path.Combine(dir, ".env");
                if (File.Exists(nestedEnv)) return nestedEnv;
            }
        }
        catch { }
        return primary; // preferred creation target
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
            string? conn = root.TryGetProperty("Project", out p) && p.TryGetProperty("DataBase", out var db) && db.TryGetProperty("ConnectionString", out var cs) ? cs.GetString() : null;
            string? id = root.TryGetProperty("Project", out p) && p.TryGetProperty("DataBase", out db) && db.TryGetProperty("RuntimeConnectionStringIdentifier", out var idEl) ? idEl.GetString() : null;
            var lines = new List<string>
            {
                "# Auto-prefilled by SpocR vNext",
                "SPOCR_GENERATOR_MODE=dual",
                ns is not null ? $"SPOCR_NAMESPACE={ns}" : "# SPOCR_NAMESPACE=Your.Project.Namespace",
                "SPOCR_OUTPUT_DIR=SpocR",
                id is not null ? $"SPOCR_DB_IDENTIFIER={id}" : "# SPOCR_DB_IDENTIFIER=DefaultConnectionName",
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

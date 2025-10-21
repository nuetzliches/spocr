using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace SpocR.SpocRVNext.Cli;

/// <summary>
/// Ensures a .env file exists when generator mode is dual or next. If missing, interactively prompts user to create one
/// or aborts and downgrades to legacy mode.
/// </summary>
internal static class EnvBootstrapper
{
    private static readonly bool Verbose = string.Equals(Environment.GetEnvironmentVariable("SPOCR_VERBOSE"), "1", StringComparison.Ordinal);
    private const string ExampleRelativePath = "samples\\restapi\\.env.example";
    private const string EnvFileName = ".env";

    /// <summary>
    /// Ensure a .env exists at <paramref name="projectRoot"/>. Can run interactively (prompt) or non-interactively (autoApprove).
    /// When force==true an existing file will be overwritten.
    /// </summary>
    public static async Task<string> EnsureEnvAsync(string projectRoot, string desiredMode, bool autoApprove = false, bool force = false, string? explicitTemplate = null)
    {
        Directory.CreateDirectory(projectRoot);
        var envPath = Path.Combine(projectRoot, EnvFileName);
        if (File.Exists(envPath) && !force)
        {
            // Enhancement: attempt in-place SPOCR_BUILD_SCHEMAS prefill if missing.
            try
            {
                var existing = File.ReadAllText(envPath);
                if (!existing.Split('\n').Any(l => l.TrimStart().StartsWith("SPOCR_BUILD_SCHEMAS", StringComparison.OrdinalIgnoreCase)))
                {
                    var inferred = InferSchemasForPrefill(projectRoot);
                    if (inferred != null && inferred.Count > 0)
                    {
                        existing += (existing.EndsWith("\n") ? string.Empty : "\n") +
                                    "SPOCR_BUILD_SCHEMAS=" + string.Join(",", inferred) + "\n";
                        File.WriteAllText(envPath, existing);
                        try { if (Verbose) Console.Out.WriteLine($"[spocr vNext] Augmented existing .env with SPOCR_BUILD_SCHEMAS={string.Join(",", inferred)}"); } catch { }
                    }
                }
            }
            catch { /* non-fatal */ }
            return envPath; // already present and not forcing (after augmentation attempt)
        }

        // Interactive approval unless autoApprove
        if (!autoApprove)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[spocr vNext] Mode '{desiredMode}' requires a {EnvFileName} with at least one SPOCR_ marker.");
            Console.ResetColor();
            Console.Write(File.Exists(envPath) ? $"Overwrite existing {EnvFileName}? [y/N]: " : "Create new .env from example now? [Y/n]: ");
            var answer = ReadAnswer();
            var proceed = IsYes(answer);
            if (!proceed)
            {
                Console.WriteLine("Falling back to legacy mode (no .env created). Set SPOCR_GENERATOR_MODE=legacy explicitly to silence this prompt.");
                Environment.SetEnvironmentVariable("SPOCR_GENERATOR_MODE", "legacy");
                return envPath; // may or may not exist
            }
        }

        try
        {
            string baseContent = ResolveExampleContent(projectRoot, explicitTemplate);
            var mergedContent = MergeWithConfig(projectRoot, baseContent);
            File.WriteAllText(envPath, mergedContent);
            if (!mergedContent.Contains("SPOCR_"))
            {
                File.AppendAllText(envPath, "# SPOCR_NAMESPACE=AddYourNamespaceHere\n");
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{(force ? "(re)created" : "Created")} {EnvFileName} at '{envPath}'.");
            Console.ResetColor();
            if (Verbose) Console.WriteLine("[spocr vNext] Next steps: (1) Review SPOCR_NAMESPACE (2) Adjust SPOCR_GENERATOR_MODE=next when comfortable (3) Re-run generation.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to create .env: {ex.Message}. Falling back to legacy mode.");
            Console.ResetColor();
            Environment.SetEnvironmentVariable("SPOCR_GENERATOR_MODE", "legacy");
        }
        await Task.CompletedTask;
        return envPath;
    }

    private static string? FindRepoRoot(string start)
    {
        try
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "README.md")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch { }
        return null;
    }

    private static string ReadAnswer()
    {
        var line = Console.ReadLine();
        return line?.Trim() ?? string.Empty;
    }
    private static bool IsYes(string input) => input.Length == 0 || input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static string ResolveExampleContent(string projectRoot, string? explicitTemplate)
    {
        if (!string.IsNullOrEmpty(explicitTemplate)) return explicitTemplate;
        var examplePath = Path.Combine(projectRoot, ExampleRelativePath);
        if (!File.Exists(examplePath))
        {
            var repoRoot = FindRepoRoot(projectRoot);
            if (repoRoot != null)
            {
                var alt = Path.Combine(repoRoot, ExampleRelativePath);
                if (File.Exists(alt)) examplePath = alt;
            }
        }
        if (File.Exists(examplePath)) return File.ReadAllText(examplePath);
        return "# SpocR vNext configuration\nSPOCR_GENERATOR_MODE=dual\n# SPOCR_NAMESPACE=Your.Project.Namespace\n# SPOCR_OUTPUT_DIR=SpocR\n";
    }

    private static string MergeWithConfig(string projectRoot, string exampleContent)
    {
        // Look for spocr.json in this directory only (already scoped earlier in EnvConfiguration)
        var cfgPath = Path.Combine(projectRoot, "spocr.json");
        string? ns = null; string? tfm = null; string? conn = null;
        List<string>? buildSchemas = null;
        if (File.Exists(cfgPath))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfgPath));
                var root = doc.RootElement;
                ns = root.TryGetProperty("Project", out var p) && p.TryGetProperty("Output", out var o) && o.TryGetProperty("Namespace", out var nsEl) ? nsEl.GetString() : null;
                tfm = root.TryGetProperty("TargetFramework", out var tfmEl) ? tfmEl.GetString() : null;
                conn = root.TryGetProperty("Project", out p) && p.TryGetProperty("DataBase", out var db) && db.TryGetProperty("ConnectionString", out var cs) ? cs.GetString() : null;
                // Collect positive schema list (case-insensitive key): all schema entries except those explicitly marked Status=Ignore.
                var schemaArr = TryGetCaseInsensitive(root, "Schema");
                if (schemaArr.HasValue && schemaArr.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var collected = new List<string>();
                    foreach (var schemaEl in schemaArr.Value.EnumerateArray())
                    {
                        try
                        {
                            string? name = schemaEl.TryGetProperty("Name", out var nEl) ? nEl.GetString() : null;
                            string? status = schemaEl.TryGetProperty("Status", out var sEl) ? sEl.GetString() : null;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            if (!string.IsNullOrWhiteSpace(status) && status.Equals("Ignore", StringComparison.OrdinalIgnoreCase)) continue; // skip ignored
                            collected.Add(name!);
                        }
                        catch { }
                    }
                    if (collected.Count > 0)
                    {
                        buildSchemas = collected;
                        try { Console.Out.WriteLine($"[spocr vNext] Prefill: SPOCR_BUILD_SCHEMAS derived from spocr.json Schema node -> {string.Join(",", buildSchemas)}"); } catch { }
                    }
                }
            }
            catch { }
        }
        // Fallback: If no explicit Build schemas collected, attempt to infer from snapshot index (prefer stable canonical set)
        if (buildSchemas == null)
        {
            try
            {
                var snapshotDir = Path.Combine(projectRoot, ".spocr", "schema");
                var indexPath = Path.Combine(snapshotDir, "index.json");
                if (File.Exists(indexPath))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(indexPath));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Procedures", out var procsEl) && procsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var procEl in procsEl.EnumerateArray())
                        {
                            try
                            {
                                // Expect shape { "OperationName": "schema.proc" }
                                string? opName = procEl.TryGetProperty("OperationName", out var opEl) ? opEl.GetString() : null;
                                if (string.IsNullOrWhiteSpace(opName)) continue;
                                var dotIdx = opName.IndexOf('.');
                                var schemaName = dotIdx > 0 ? opName.Substring(0, dotIdx) : "dbo";
                                if (!string.IsNullOrWhiteSpace(schemaName)) set.Add(schemaName);
                            }
                            catch { }
                        }
                        if (set.Count > 0) buildSchemas = set.OrderBy(s => s).ToList();
                    }
                }
            }
            catch { /* snapshot inference skipped */ }
        }
        // Extended fallback: scan expanded snapshot layout (procedures/*.json) if index.json absent or did not yield schemas
        if (buildSchemas == null)
        {
            try
            {
                var snapshotDir = Path.Combine(projectRoot, ".spocr", "schema", "procedures");
                if (Directory.Exists(snapshotDir))
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in Directory.EnumerateFiles(snapshotDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                            var root = doc.RootElement;
                            string? opName = root.TryGetProperty("OperationName", out var opEl) ? opEl.GetString() : null;
                            if (string.IsNullOrWhiteSpace(opName)) continue;
                            var dotIdx = opName.IndexOf('.');
                            var schemaName = dotIdx > 0 ? opName.Substring(0, dotIdx) : "dbo";
                            if (!string.IsNullOrWhiteSpace(schemaName)) set.Add(schemaName);
                        }
                        catch { }
                    }
                    if (set.Count > 0) buildSchemas = set.OrderBy(s => s).ToList();
                }
            }
            catch { /* expanded scan skipped */ }
        }
        // Simple line map override: keep comments, replace key lines if present, append if missing.
        var lines = exampleContent.Replace("\r\n", "\n").Split('\n');
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                var key = line.Substring(0, eq).Trim();
                if (key.StartsWith("SPOCR_", StringComparison.OrdinalIgnoreCase) && !dict.ContainsKey(key)) dict[key] = i;
            }
        }
        void Upsert(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var line = key + "=" + value;
            if (dict.TryGetValue(key, out var idx)) lines[idx] = line;
            else
            {
                // append before final separator block if exists
                var list = lines.ToList();
                list.Add(line);
                lines = list.ToArray();
            }
        }
        Upsert("SPOCR_NAMESPACE", ns);
        Upsert("SPOCR_TFM", tfm);
        Upsert("SPOCR_GENERATOR_DB", conn);
        // Insert SPOCR_BUILD_SCHEMAS (comma separated) if we have any inferred/explicit build schemas; else add placeholder comment
        if (buildSchemas != null && buildSchemas.Count > 0)
        {
            Upsert("SPOCR_BUILD_SCHEMAS", string.Join(",", buildSchemas.Distinct(StringComparer.OrdinalIgnoreCase)));
        }
        else
        {
            if (!dict.ContainsKey("SPOCR_BUILD_SCHEMAS"))
            {
                var listLines = lines.ToList();
                listLines.Add("# SPOCR_BUILD_SCHEMAS=SchemaA,SchemaB (optional positive allow-list; empty -> all except ignored)");
                lines = listLines.ToArray();
            }
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    // Helper: case-insensitive property lookup
    private static System.Text.Json.JsonElement? TryGetCaseInsensitive(System.Text.Json.JsonElement root, string name)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) return prop.Value;
        }
        return null;
    }

    // Helper: reuse schema inference for augmentation
    private static List<string>? InferSchemasForPrefill(string projectRoot)
    {
        List<string>? buildSchemas = null;
        var cfgPath = Path.Combine(projectRoot, "spocr.json");
        if (File.Exists(cfgPath))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfgPath));
                var root = doc.RootElement;
                var schemaArr = TryGetCaseInsensitive(root, "Schema");
                if (schemaArr.HasValue && schemaArr.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var schemaEl in schemaArr.Value.EnumerateArray())
                    {
                        try
                        {
                            string? name = schemaEl.TryGetProperty("Name", out var nEl) ? nEl.GetString() : null;
                            string? status = schemaEl.TryGetProperty("Status", out var sEl) ? sEl.GetString() : null;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            if (!string.IsNullOrWhiteSpace(status) && status.Equals("Ignore", StringComparison.OrdinalIgnoreCase)) continue;
                            buildSchemas ??= new List<string>();
                            buildSchemas.Add(name!);
                        }
                        catch { }
                    }
                    if (buildSchemas != null && buildSchemas.Count > 0) return buildSchemas.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
                }
            }
            catch { }
        }
        // Snapshot fallback (index.json)
        try
        {
            var snapshotDir = Path.Combine(projectRoot, ".spocr", "schema");
            var indexPath = Path.Combine(snapshotDir, "index.json");
            if (buildSchemas == null && File.Exists(indexPath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(indexPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("Procedures", out var procsEl) && procsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var procEl in procsEl.EnumerateArray())
                    {
                        try
                        {
                            string? opName = procEl.TryGetProperty("OperationName", out var opEl) ? opEl.GetString() : null;
                            if (string.IsNullOrWhiteSpace(opName)) continue;
                            var dotIdx = opName.IndexOf('.');
                            var schemaName = dotIdx > 0 ? opName.Substring(0, dotIdx) : "dbo";
                            if (!string.IsNullOrWhiteSpace(schemaName))
                            {
                                buildSchemas ??= new List<string>();
                                buildSchemas.Add(schemaName);
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        // Expanded layout (procedures/*.json)
        try
        {
            if (buildSchemas == null)
            {
                var procDir = Path.Combine(projectRoot, ".spocr", "schema", "procedures");
                if (Directory.Exists(procDir))
                {
                    foreach (var file in Directory.EnumerateFiles(procDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                            var root = doc.RootElement;
                            string? opName = root.TryGetProperty("OperationName", out var opEl) ? opEl.GetString() : null;
                            if (string.IsNullOrWhiteSpace(opName)) continue;
                            var dotIdx = opName.IndexOf('.');
                            var schemaName = dotIdx > 0 ? opName.Substring(0, dotIdx) : "dbo";
                            if (!string.IsNullOrWhiteSpace(schemaName))
                            {
                                buildSchemas ??= new List<string>();
                                buildSchemas.Add(schemaName);
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        if (buildSchemas != null && buildSchemas.Count > 0)
            return buildSchemas.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
        return null;
    }
}

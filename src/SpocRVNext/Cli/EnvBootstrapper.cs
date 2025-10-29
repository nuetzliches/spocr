using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace SpocR.SpocRVNext.Cli;

/// <summary>
/// Ensures a .env file exists for the next-only generator. If missing, interactively prompts the user to create one
/// and aborts if they decline.
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
    public static async Task<string> EnsureEnvAsync(string projectRoot, bool autoApprove = false, bool force = false, string? explicitTemplate = null)
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
            Console.WriteLine($"[spocr vNext] Next-only generator requires a {EnvFileName} with at least one SPOCR_ marker.");
            Console.ResetColor();
            Console.Write(File.Exists(envPath) ? $"Overwrite existing {EnvFileName}? [y/N]: " : "Create new .env from example now? [Y/n]: ");
            var answer = ReadAnswer();
            var proceed = IsYes(answer);
            if (!proceed)
            {
                throw new InvalidOperationException(".env creation aborted by user – SpocR vNext requires an .env file.");
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
            if (Verbose) Console.WriteLine("[spocr vNext] Next steps: (1) Review SPOCR_NAMESPACE (2) Re-run generation.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to create .env: {ex.Message}.");
            Console.ResetColor();
            throw;
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
       return "# SpocR vNext configuration\n"
           + "# SPOCR_NAMESPACE=Your.Project.Namespace\n"
           + "# SPOCR_GENERATOR_DB=Server=localhost;Database=AppDb;Trusted_Connection=True;TrustServerCertificate=True;\n"
           + "# SPOCR_OUTPUT_DIR=SpocR\n"
           + "# SPOCR_BUILD_SCHEMAS=SchemaA,SchemaB\n";
    }

    private static string MergeWithConfig(string projectRoot, string exampleContent)
    {
        var buildSchemas = InferSchemasForPrefill(projectRoot);
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
        // Without legacy configuration we only prefill build schema hints; namespace/TFM/connection remain placeholders.
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

    // Helper: reuse schema inference for augmentation
    private static List<string>? InferSchemasForPrefill(string projectRoot)
    {
        List<string>? buildSchemas = null;
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

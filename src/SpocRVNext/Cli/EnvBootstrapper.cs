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
            return envPath; // already present and not forcing

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
            Console.WriteLine("[spocr vNext] Next steps: (1) Review SPOCR_NAMESPACE (2) Adjust SPOCR_GENERATOR_MODE=next when comfortable (3) Re-run generation.");
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
        if (File.Exists(cfgPath))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfgPath));
                var root = doc.RootElement;
                ns = root.TryGetProperty("Project", out var p) && p.TryGetProperty("Output", out var o) && o.TryGetProperty("Namespace", out var nsEl) ? nsEl.GetString() : null;
                tfm = root.TryGetProperty("TargetFramework", out var tfmEl) ? tfmEl.GetString() : null;
                conn = root.TryGetProperty("Project", out p) && p.TryGetProperty("DataBase", out var db) && db.TryGetProperty("ConnectionString", out var cs) ? cs.GetString() : null;
            }
            catch { }
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
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

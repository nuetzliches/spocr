using System;
using System.IO;
using System.Threading.Tasks;

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
            string content;
            if (!string.IsNullOrEmpty(explicitTemplate))
            {
                content = explicitTemplate;
            }
            else
            {
                var examplePath = Path.Combine(projectRoot, ExampleRelativePath);
                if (!File.Exists(examplePath))
                {
                    // Try walking up (support running from sample child folder)
                    var repoRoot = FindRepoRoot(projectRoot);
                    if (repoRoot != null)
                    {
                        var alt = Path.Combine(repoRoot, ExampleRelativePath);
                        if (File.Exists(alt)) examplePath = alt;
                    }
                }
                if (File.Exists(examplePath))
                    content = File.ReadAllText(examplePath);
                else
                    content = "# SpocR vNext configuration\n# SPOCR_GENERATOR_MODE=dual\n# SPOCR_NAMESPACE=Your.Project.Namespace\n# SPOCR_OUTPUT_DIR=SpocR\n";
            }
            File.WriteAllText(envPath, content);
            // Ensure at least one marker
            if (!content.Contains("SPOCR_"))
            {
                File.AppendAllText(envPath, "# SPOCR_NAMESPACE=AddYourNamespaceHere\n");
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{(force ? "(re)created" : "Created")} {EnvFileName} at '{envPath}'.");
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
}

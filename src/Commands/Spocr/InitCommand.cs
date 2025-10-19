using System;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Utils;
using SpocR.Infrastructure;

namespace SpocR.Commands.Spocr;

/// <summary>
/// v5+ initialization command replacing legacy 'create'.
/// Creates or updates a .env file (never writes a new spocr.json) and allows overriding
/// core SPOCR_* keys via CLI flags. Safe to invoke multiple times (idempotent except for ordering of appended keys).
/// </summary>
[HelpOption("-?|-h|--help")]
[Command("init", Description = "Initialize SpocR project (.env bootstrap). Replaces legacy 'create' in v5.")]
public class InitCommand : SpocrCommandBase
{
    [Option("-n|--namespace", "Root namespace (SPOCR_NAMESPACE)", CommandOptionType.SingleValue)]
    public string RootNamespace { get; set; }

    [Option("-m|--mode", "Generator mode (legacy|dual|next). In v5 'next' is default.", CommandOptionType.SingleValue)]
    public string Mode { get; set; }

    [Option("-c|--connection", "Metadata pull connection string (SPOCR_GENERATOR_DB)", CommandOptionType.SingleValue)]
    public string ConnectionString { get; set; }

    [Option("-s|--schemas", "Comma separated allow-list (SPOCR_BUILD_SCHEMAS)", CommandOptionType.SingleValue)]
    public string Schemas { get; set; }

    public InitCommand(SpocR.Managers.SpocrProjectManager projectManager) : base(projectManager) { }

    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        var effectivePath = string.IsNullOrWhiteSpace(Path) ? Directory.GetCurrentDirectory() : Path;
        var dir = DirectoryUtils.IsPath(effectivePath) ? effectivePath : System.IO.Path.GetFullPath(effectivePath);
        Directory.CreateDirectory(dir);
        var desiredMode = string.IsNullOrWhiteSpace(Mode) ? (Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE") ?? "dual") : Mode.Trim();

        // Use existing EnvBootstrapper to materialize base file (autoApprove to skip interactive prompt here)
        var envPath = await SpocR.SpocRVNext.Cli.EnvBootstrapper.EnsureEnvAsync(dir, desiredMode, autoApprove: true, force: Force);

        try
        {
            var lines = File.ReadAllLines(envPath);
            string Upsert(string key, string value)
            {
                bool replaced = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var l = lines[i];
                    if (l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = key + "=" + value;
                        replaced = true;
                        break;
                    }
                }
                if (!replaced)
                {
                    lines = lines.Concat(new[] { key + "=" + value }).ToArray();
                }
                return key + "=" + value;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(RootNamespace)) Upsert("SPOCR_NAMESPACE", RootNamespace.Trim());
            if (!string.IsNullOrWhiteSpace(ConnectionString)) Upsert("SPOCR_GENERATOR_DB", ConnectionString.Trim());
            if (!string.IsNullOrWhiteSpace(Schemas)) Upsert("SPOCR_BUILD_SCHEMAS", string.Join(',', Schemas.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)));
            if (!string.IsNullOrWhiteSpace(Mode)) Upsert("SPOCR_GENERATOR_MODE", desiredMode);
            File.WriteAllLines(envPath, lines);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[spocr init] Warning: post-processing .env failed: {ex.Message}");
        }

        Console.WriteLine($"[spocr init] .env ready at {envPath}");
        Console.WriteLine("Next: run 'spocr pull' then 'spocr build' (or 'spocr rebuild') to generate code.");
        return ExitCodes.Success;
    }
}

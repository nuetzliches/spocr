using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.CodeGenerators;
using SpocR.Managers;
using SpocR.Utils;

namespace SpocR.Commands.Spocr;

/// <summary>
/// Interface for build command options
/// </summary>
public interface IBuildCommandOptions : ICommandOptions
{
    /// <summary>
    /// Generator types that should be activated during the build
    /// </summary>
    GeneratorTypes GeneratorTypes { get; }
}

[HelpOption("-?|-h|--help")]
[Command(
    "build",
    Description = "Generate vNext client code from current snapshots using .env",
    ExtendedHelpText = "Configures output via .env (use 'spocr init' to scaffold). JSON helpers generate by default; no preview flags needed.")]
public class BuildCommand(
    SpocrManager spocrManager,
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager), IBuildCommandOptions
{
    [Option("--generators", "Generator types to execute (TableTypes,Inputs,Models,StoredProcedures)", CommandOptionType.SingleValue)]
    public string GeneratorTypesString { get; set; }

    public GeneratorTypes GeneratorTypes
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GeneratorTypesString))
                return GeneratorTypes.All;

            GeneratorTypes result = GeneratorTypes.None;
            foreach (var typeName in GeneratorTypesString.Split(','))
            {
                if (System.Enum.TryParse<GeneratorTypes>(typeName.Trim(), out var generatorType))
                    result |= generatorType;
            }

            return result == GeneratorTypes.None ? GeneratorTypes.All : result;
        }
    }

    public override async Task<int> OnExecuteAsync()
    {
    // Legacy project alias support: resolve configured path (still stored as config file entry)
        if (!string.IsNullOrEmpty(Project))
        {
            var project = spocrProjectManager.FindByName(Project);
            if (project != null)
                Path = project.ConfigFile;
        }
        else if (!string.IsNullOrEmpty(Path) && !DirectoryUtils.IsPath(Path))
        {
            var project = spocrProjectManager.FindByName(Path);
            Path = project.ConfigFile;
        }

        await base.OnExecuteAsync();
        var result = await spocrManager.BuildAsync(this);
        return CommandResultMapper.Map(result); // unified exit code mapping
    }
}

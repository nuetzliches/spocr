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
[Command("build", Description = "Build DataContext depending on spocr.json")]
public class BuildCommand(
    SpocrManager spocrManager,
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager), IBuildCommandOptions
{
    [Option("--generators", "Generator types to execute (TableTypes,Inputs,Outputs,Models,StoredProcedures)", CommandOptionType.SingleValue)]
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
        // Read Path to spocr.json from Project configuration
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
        return (int)await spocrManager.BuildAsync(this);
    }
}

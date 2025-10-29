using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.CodeGenerators;
using SpocR.Runtime;

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
    SpocrCliRuntime cliRuntime
) : SpocrCommandBase, IBuildCommandOptions
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
        await base.OnExecuteAsync();
        var result = await cliRuntime.BuildAsync(this);
        return CommandResultMapper.Map(result); // unified exit code mapping
    }
}

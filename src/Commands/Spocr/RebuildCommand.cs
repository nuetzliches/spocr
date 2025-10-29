using McMaster.Extensions.CommandLineUtils;
using SpocR.Enums;
using SpocR.Runtime;
using System.Threading.Tasks;

namespace SpocR.Commands.Spocr;

[HelpOption("-?|-h|--help")]
[Command(
    "rebuild",
    Description = "Shortcut for pull+build using .env configuration (metadata + client code)",
    ExtendedHelpText = "Runs pull then build with your .env. Ensure SPOCR_GENERATOR_DB is set; JSON helpers are always generated.")]
public class RebuildCommand(
    SpocrCliRuntime cliRuntime
) : SpocrCommandBase
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();

        var pullResult = await cliRuntime.PullAsync(CommandOptions);
        if (pullResult != ExecuteResultEnum.Succeeded)
        {
            return CommandResultMapper.Map(pullResult);
        }

        var buildResult = await cliRuntime.BuildAsync(CommandOptions);
        return CommandResultMapper.Map(buildResult);
    }
}

using McMaster.Extensions.CommandLineUtils;
using SpocR.Enums;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Spocr;

[HelpOption("-?|-h|--help")]
[Command(
    "rebuild",
    Description = "Shortcut for pull+build using .env configuration (metadata + client code)",
    ExtendedHelpText = "Runs pull then build with your .env. Ensure SPOCR_GENERATOR_DB is set; JSON helpers are always generated.")]
public class RebuildCommand(
    SpocrManager spocrManager,
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager)
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();

        var pullResult = await spocrManager.PullAsync(CommandOptions);
        if (pullResult != ExecuteResultEnum.Succeeded)
        {
            return CommandResultMapper.Map(pullResult);
        }

        var buildResult = await spocrManager.BuildAsync(CommandOptions);
        return CommandResultMapper.Map(buildResult);
    }
}

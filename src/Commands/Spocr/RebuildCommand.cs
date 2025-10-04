using McMaster.Extensions.CommandLineUtils;
using SpocR.Enums;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Spocr;

[HelpOption("-?|-h|--help")]
[Command("rebuild", Description = "Pull DB Schema and Build DataContext")]
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

        await spocrManager.ReloadConfigurationAsync();

        var buildResult = await spocrManager.BuildAsync(CommandOptions);
        return CommandResultMapper.Map(buildResult);
    }
}

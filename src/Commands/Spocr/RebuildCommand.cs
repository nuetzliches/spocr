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

        if (await spocrManager.PullAsync(CommandOptions) == EExecuteResult.Succeeded
            && await spocrManager.BuildAsync(CommandOptions) == EExecuteResult.Succeeded)
            return (int)EExecuteResult.Succeeded;
        else
            return (int)EExecuteResult.Error;
    }
}

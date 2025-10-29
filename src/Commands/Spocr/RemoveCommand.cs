using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Spocr;

[HelpOption("-?|-h|--help")]
[Command("remove", Description = "Deprecated: legacy cleanup helper (manual removal recommended)")]
public class RemoveCommand(
    SpocrManager spocrManager,
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager)
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        var result = await spocrManager.RemoveAsync(CommandOptions);
        return CommandResultMapper.Map(result);
    }
}

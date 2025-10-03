using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Spocr;

[HelpOption("-?|-h|--help")]
[Command("config", Description = "Configure SpocR")]
public class ConfigCommand(
    SpocrConfigManager spocrConfigManager,
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager)
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        var result = await spocrConfigManager.ConfigAsync();
        return CommandResultMapper.Map(result);
    }
}

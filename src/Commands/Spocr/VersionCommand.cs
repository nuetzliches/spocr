using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Spocr;

[Command("version", Description = "Show version information")]
public class VersionCommand(
    SpocrManager spocrManager,
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager)
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)await spocrManager.GetVersionAsync();
    }
}

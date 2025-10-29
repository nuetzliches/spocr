using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Runtime;

namespace SpocR.Commands.Spocr;

[Command("version", Description = "Show version information")]
public class VersionCommand(
    SpocrCliRuntime cliRuntime
) : SpocrCommandBase
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)await cliRuntime.GetVersionAsync();
    }
}

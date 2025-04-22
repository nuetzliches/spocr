using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Project;

[HelpOption("-?|-h|--help")]
[Command("ls", Description = "List all SpocR Projects")]
public class ProjectListCommand(
    SpocrProjectManager spocrProjectManager
) : ProjectCommandBase
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)await spocrProjectManager.ListAsync(CommandOptions);
    }
}

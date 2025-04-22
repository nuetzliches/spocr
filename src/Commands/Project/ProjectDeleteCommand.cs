using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Project;

[HelpOption("-?|-h|--help")]
[Command("delete", Description = "Delete an existing SpocR Project")]
public class ProjectDeleteCommand(
    SpocrProjectManager spocrProjectManager
) : ProjectCommandBase
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)await spocrProjectManager.DeleteAsync(ProjectCommandOptions);
    }
}

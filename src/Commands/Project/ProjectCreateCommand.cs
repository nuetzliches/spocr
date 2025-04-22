using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Project;

[HelpOption("-?|-h|--help")]
[Command("create", Description = "Creates a new SpocR Project")]
public class ProjectCreateCommand(
    SpocrProjectManager spocrProjectManager
) : ProjectCommandBase
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)await spocrProjectManager.CreateAsync(ProjectCommandOptions);
    }
}

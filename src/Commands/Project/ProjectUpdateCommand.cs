using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Project;

[HelpOption("-?|-h|--help")]
[Command("update", Description = "Update an existing SpocR Project")]
public class ProjectUpdateCommand(
    SpocrProjectManager spocrProjectManager
) : ProjectCommandBase, IProjectUpdateCommandOptions
{
    [Option("-nn|--new-name", "New Project name and identifier", CommandOptionType.SingleValue)]
    public string NewDisplayName { get; set; }

    public ProjectUpdateCommandOptions ProjectUpdateCommandOptions => new(this);

    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)await spocrProjectManager.UpdateAsync(ProjectUpdateCommandOptions);
    }
}

public interface IProjectUpdateCommandOptions : IProjectCommandOptions
{
    string NewDisplayName { get; }
}

public class ProjectUpdateCommandOptions(
    IProjectUpdateCommandOptions options
) : ProjectCommandOptions(options), IProjectUpdateCommandOptions
{
    public string NewDisplayName => options.NewDisplayName?.Trim();
}

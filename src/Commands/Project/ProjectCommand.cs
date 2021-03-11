using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Project
{
    [HelpOption("-?|-h|--help")]
    [Command("project", Description = "Project configuration")]
    [Subcommand(typeof(ProjectCreateCommand))]
    [Subcommand(typeof(ProjectUpdateCommand))]
    [Subcommand(typeof(ProjectDeleteCommand))]
    [Subcommand(typeof(ProjectListCommand))]
    public class ProjectCommand : CommandBase
    {
    }
}

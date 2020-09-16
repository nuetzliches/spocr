using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Project
{
    [HelpOption("-?|-h|--help")]
    [Command("project", Description = "Project configuration")]
    [Subcommand("create", typeof(ProjectCreateCommand))]
    [Subcommand("update", typeof(ProjectUpdateCommand))]
    [Subcommand("delete", typeof(ProjectDeleteCommand))]
    [Subcommand("ls", typeof(ProjectListCommand))]
    public class ProjectCommand : CommandBase
    {
    }
}

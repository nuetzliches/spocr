using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Project
{
    [HelpOption("-?|-h|--help")]
    [Command("create", Description = "Creates a new SpocR Project")]
    public class ProjectCreateCommand : ProjectCommandBase
    {
        public ProjectCreateCommand(SpocrProjectManager spocrProjectManager)
        : base(spocrProjectManager)
        { }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)SpocrProjectManager.Create(ProjectCommandOptions);
        }
    }
}

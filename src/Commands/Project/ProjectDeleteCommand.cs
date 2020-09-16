using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Project
{
    [HelpOption("-?|-h|--help")]
    [Command("delete", Description = "Delete an existing SpocR Project")]
    public class ProjectDeleteCommand : ProjectCommandBase
    {
        public ProjectDeleteCommand(SpocrProjectManager spocrProjectManager)
        : base(spocrProjectManager)
        { }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)SpocrProjectManager.Delete(ProjectCommandOptions);
        }
    }
}

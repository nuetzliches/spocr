using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Project
{
    [HelpOption("-?|-h|--help")]
    [Command("ls", Description = "List all SpocR Projects")]
    public class ProjectListCommand : ProjectCommandBase
    {
        public ProjectListCommand(SpocrProjectManager spocrProjectManager)
        : base(spocrProjectManager)
        { }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)SpocrProjectManager.List(CommandOptions);
        }
    }
}

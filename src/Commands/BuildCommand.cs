using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("build", Description = "Build DataContex depending on spocr.json")]
    public class BuildCommand : CommandBase
    {
        private readonly SpocrManager _spocrManager;

        public BuildCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)_spocrManager.Build(DryRun);
        }
    }
}

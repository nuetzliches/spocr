using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Spocr
{
    [HelpOption("-?|-h|--help")]
    [Command("build", Description = "Build DataContex depending on spocr.json")]
    public class BuildCommand : SpocrCommandBase
    {
        private readonly SpocrManager _spocrManager;

        public BuildCommand(SpocrManager spocrManager, SpocrProjectManager spocrProjectManager) 
        : base(spocrProjectManager)
        {
            _spocrManager = spocrManager;
        }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)_spocrManager.Build(CommandOptions);
        }
    }
}

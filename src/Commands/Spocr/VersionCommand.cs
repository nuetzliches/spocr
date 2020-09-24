using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Spocr
{
    [Command("version", Description = "Show version information")]
    public class VersionCommand : SpocrCommandBase
    {
        private readonly SpocrManager _spocrManager;

        public VersionCommand(SpocrManager spocrManager, SpocrProjectManager spocrProjectManager) 
        : base(spocrProjectManager)
        {
            _spocrManager = spocrManager;
        }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)_spocrManager.GetVersion(CommandOptions);
        }
    }
}

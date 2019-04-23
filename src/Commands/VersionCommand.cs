using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [Command("version", Description = "Show version information")]
    public class VersionCommand : CommandBase
    {
        private readonly SpocrManager _spocrManager;

        public VersionCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)_spocrManager.GetVersion();
        }
    }
}

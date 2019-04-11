using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [Command("version", Description = "Show version information")]
    public class VersionCommand : IAppCommand
    {
        private readonly SpocrManager _spocrManager;

        public VersionCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public int OnExecute()
        {
            return (int)_spocrManager.GetVersion();
        }
    }
}

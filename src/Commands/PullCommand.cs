
using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("pull", Description = "Pull all schema informations from DB into spocr.json")]
    public class PullCommand : IAppCommand
    {
        private readonly SpocrManager _spocrManager;

        [Option("-d|--dry-run", "Run pull without any changes", CommandOptionType.NoValue)]
        public bool DryRun { get; set; }

        public PullCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public int OnExecute()
        {
            return (int)_spocrManager.Pull(DryRun);
        }
    }
}

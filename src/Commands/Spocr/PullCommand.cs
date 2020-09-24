using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Spocr
{
    [HelpOption("-?|-h|--help")]
    [Command("pull", Description = "Pull all schema informations from DB into spocr.json")]
    public class PullCommand : SpocrCommand
    {
        private readonly SpocrManager _spocrManager;

        public PullCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)_spocrManager.Pull(CommandOptions);
        }
    }
}

using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("create", Description = "Creates a new SpocR Project")]
    public class CreateCommand : CommandBase
    {
        private readonly SpocrManager _spocrManager;

        public CreateCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;

        }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)_spocrManager.Create(CommandOptions);
        }
    }
}

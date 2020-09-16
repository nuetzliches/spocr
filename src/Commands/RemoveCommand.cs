using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("remove", Description = "Removes the SpocR Project")]
    public class RemoveCommand : CommandBase
    {
        private readonly SpocrManager _spocrManager;

        public RemoveCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)_spocrManager.Remove(CommandOptions);
        }
    }
}

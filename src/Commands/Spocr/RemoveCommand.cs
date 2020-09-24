using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Spocr
{
    [HelpOption("-?|-h|--help")]
    [Command("remove", Description = "Removes the SpocR Project")]
    public class RemoveCommand : SpocrCommandBase
    {
        private readonly SpocrManager _spocrManager;

        public RemoveCommand(SpocrManager spocrManager, SpocrProjectManager spocrProjectManager) 
        : base(spocrProjectManager)
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

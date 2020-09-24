using McMaster.Extensions.CommandLineUtils;
using SpocR.Enums;
using SpocR.Managers;

namespace SpocR.Commands.Spocr
{
    [HelpOption("-?|-h|--help")]
    [Command("rebuild", Description = "Pull DB Schema and Build DataContext")]
    public class RebuildCommand : SpocrCommandBase
    {
        private readonly SpocrManager _spocrManager;

        public RebuildCommand(SpocrManager spocrManager, SpocrProjectManager spocrProjectManager) 
        : base(spocrProjectManager)
        {
            _spocrManager = spocrManager;
        }

        public override int OnExecute()
        {
            base.OnExecute();

            if (_spocrManager.Pull(CommandOptions) == ExecuteResultEnum.Succeeded
                && _spocrManager.Build(CommandOptions) == ExecuteResultEnum.Succeeded)
                return (int)ExecuteResultEnum.Succeeded;
            else
                return (int)ExecuteResultEnum.Error;
        }
    }
}

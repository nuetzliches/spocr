using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Spocr
{
    [HelpOption("-?|-h|--help")]
    [Command("config", Description = "Configure SpocR")]
    public class ConfigCommand : SpocrCommandBase
    {
        private readonly SpocrConfigManager _spocrConfigManager;

        public ConfigCommand(SpocrConfigManager spocrConfigManager, SpocrProjectManager spocrProjectManager) 
        : base(spocrProjectManager)
        {
            _spocrConfigManager = spocrConfigManager;
        }

        public override int OnExecute()
        {
            base.OnExecute();         
            return (int)_spocrConfigManager.Config();
        }
    }
}

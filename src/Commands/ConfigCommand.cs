using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("config", Description = "Configure SpocR")]
    public class ConfigCommand : CommandBase
    {
        private readonly SpocrConfigManager _spocrConfigManager;

        public ConfigCommand(SpocrConfigManager spocrConfigManager)
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

using McMaster.Extensions.CommandLineUtils;
using SpocR.Enums;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("rebuild", Description = "Pull DB Schema and Build DataContext")]
    public class RebuildCommand : IAppCommand
    {
        private readonly SpocrManager _spocrManager;

        [Option("-d|--dry-run", "Run build without any changes", CommandOptionType.NoValue)]
        public bool DryRun { get; set; }

        public RebuildCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public int OnExecute()
        {
            if (_spocrManager.Pull(DryRun) == ExecuteResultEnum.Succeeded
                && _spocrManager.Build(DryRun) == ExecuteResultEnum.Succeeded)
                return (int)ExecuteResultEnum.Succeeded;
            else
                return (int)ExecuteResultEnum.Error;
        }
    }
}

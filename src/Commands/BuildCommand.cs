
using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("build", Description = "Build DataContex depending on spocr.json")]
    public class BuildCommand : IAppCommand
    {
        private readonly SpocrManager _spocrManager;

        [Option("-d|--dry-run", "Run build without any changes", CommandOptionType.NoValue)]
        public bool DryRun { get; set; }

        public BuildCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public int OnExecute()
        {
            return (int)_spocrManager.Build(DryRun);
        }
    }
}


using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("create", Description = "Creates a new SpocR Project")]
    public class CreateCommand : IAppCommand
    {
        private readonly SpocrManager _spocrManager;

        [Option("-d|--dry-run", "Run create without any changes", CommandOptionType.NoValue)]
        public bool DryRun { get; set; }

        public CreateCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public int OnExecute()
        {
            return (int)_spocrManager.Create(DryRun);
        }
    }
}

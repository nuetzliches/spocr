
using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("remove", Description = "Removes the SpocR Project")]
    public class RemoveCommand : IAppCommand
    {
        private readonly SpocrManager _spocrManager;


        [Option("-d|--dry-run", "Run remove without any changes", CommandOptionType.NoValue)]
        public bool DryRun { get; set; }

        public RemoveCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public int OnExecute()
        {
            return (int)_spocrManager.Remove(DryRun);
        }
    }
}

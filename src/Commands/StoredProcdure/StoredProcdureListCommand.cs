using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.StoredProcdure
{
    [HelpOption("-?|-h|--help")]
    [Command("ls", Description = "List all SpocR StoredProcdures")]
    public class StoredProcdureListCommand : StoredProcdureCommandBase
    {
        public StoredProcdureListCommand(SpocrStoredProcdureManager spocrStoredProcdureManager)
        : base(spocrStoredProcdureManager)
        { }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)SpocrStoredProcdureManager.List(StoredProcedureCommandOptions);
        }
    }
}

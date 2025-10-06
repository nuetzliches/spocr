using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.StoredProcedure;

[HelpOption("-?|-h|--help")]
[Command("ls", Description = "List all SpocR StoredProcedures")]
public class StoredProcedureListCommand(
    SpocrStoredProcedureManager spocrStoredProcedureManager,
    SpocrProjectManager spocrProjectManager
) : StoredProcedureCommandBase(spocrProjectManager)
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)spocrStoredProcedureManager.List(StoredProcedureCommandOptions);
    }
}

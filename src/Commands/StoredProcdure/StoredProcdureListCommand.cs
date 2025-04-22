using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.StoredProcdure;

[HelpOption("-?|-h|--help")]
[Command("ls", Description = "List all SpocR StoredProcdures")]
public class StoredProcdureListCommand(
    SpocrStoredProcdureManager spocrStoredProcdureManager,
    SpocrProjectManager spocrProjectManager
) : StoredProcdureCommandBase(spocrProjectManager)
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)spocrStoredProcdureManager.List(StoredProcedureCommandOptions);
    }
}

using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Schema;

[HelpOption("-?|-h|--help")]
[Command("ls", Description = "List all SpocR Schemas")]
public class SchemaListCommand(
    SpocrSchemaManager spocrSchemaManager,
    SpocrProjectManager spocrProjectManager
) : SchemaCommandBase(spocrProjectManager)
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)await spocrSchemaManager.ListAsync(CommandOptions);
    }
}

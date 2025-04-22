using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Schema;

[HelpOption("-?|-h|--help")]
[Command("update", Description = "Update an existing SpocR Schema")]
public class SchemaUpdateCommand(
    SpocrSchemaManager spocrSchemaManager,
    SpocrProjectManager spocrProjectManager
) : SchemaCommandBase(spocrProjectManager), ISchemaUpdateCommandOptions
{
    [Option("--name", "Schema name", CommandOptionType.SingleValue)]
    public string SchemaName { get; set; }

    [Option("--status", "Set schema status to Build or Ignored", CommandOptionType.SingleValue)]
    public string Status { get; set; }

    public SchemaUpdateCommandOptions SchemaUpdateCommandOptions => new(this);

    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)await spocrSchemaManager.UpdateAsync(SchemaUpdateCommandOptions);
    }
}

public interface ISchemaUpdateCommandOptions : ICommandOptions
{
    string SchemaName { get; }
    string Status { get; }
}

public class SchemaUpdateCommandOptions(
    ISchemaUpdateCommandOptions options
) : CommandOptions(options), ISchemaUpdateCommandOptions
{
    public string SchemaName => options.SchemaName?.Trim();
    public string Status => options.Status?.Trim();
}

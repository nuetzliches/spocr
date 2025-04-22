using McMaster.Extensions.CommandLineUtils;

namespace SpocR.Commands.Schema;

[HelpOption("-?|-h|--help")]
[Command("schema", Description = "Schema configuration")]
[Subcommand(typeof(SchemaUpdateCommand))]
[Subcommand(typeof(SchemaListCommand))]
public class SchemaCommand : CommandBase
{
}

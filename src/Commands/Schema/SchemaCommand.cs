using McMaster.Extensions.CommandLineUtils;

namespace SpocR.Commands.Schema
{
    [HelpOption("-?|-h|--help")]
    [Command("schema", Description = "Schema configuration")]
    [Subcommand("update", typeof(SchemaUpdateCommand))]
    [Subcommand("ls", typeof(SchemaListCommand))]
    public class SchemaCommand : CommandBase
    {
    }
}

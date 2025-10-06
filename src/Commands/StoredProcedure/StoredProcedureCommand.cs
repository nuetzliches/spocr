using McMaster.Extensions.CommandLineUtils;

namespace SpocR.Commands.StoredProcedure;

[HelpOption("-?|-h|--help")]
[Command("sp", Description = "StoredProcedure informations and configuration")]
[Subcommand(typeof(StoredProcedureListCommand))]
public class StoredProcedureCommand : CommandBase
{
}

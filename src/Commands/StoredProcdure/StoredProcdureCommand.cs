using McMaster.Extensions.CommandLineUtils;

namespace SpocR.Commands.StoredProcdure
{
    [HelpOption("-?|-h|--help")]
    [Command("sp", Description = "StoredProcdure informations and configuration")]
    [Subcommand("ls", typeof(StoredProcdureListCommand))]
    public class StoredProcdureCommand : CommandBase
    {
    }
}

using McMaster.Extensions.CommandLineUtils;

namespace SpocR.Commands
{
    [Command("version", Description = "Show version information")]
    public class VersionCommand : IAppCommand
    {
        public int OnExecute()
        {
            return 1;
        }
    }
}

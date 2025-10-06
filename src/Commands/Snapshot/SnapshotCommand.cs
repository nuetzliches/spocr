using McMaster.Extensions.CommandLineUtils;

namespace SpocR.Commands.Snapshot;

[HelpOption("-?|-h|--help")]
[Command("snapshot", Description = "Manage schema snapshot files (.spocr/schema)")]
[Subcommand(typeof(SnapshotCleanCommand))]
public class SnapshotCommand : CommandBase
{
}

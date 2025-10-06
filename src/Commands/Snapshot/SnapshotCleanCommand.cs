using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;

namespace SpocR.Commands.Snapshot;

[HelpOption("-?|-h|--help")]
[Command("clean", Description = "Delete old snapshot files (default keep latest 5)")]
public class SnapshotCleanCommand(
    SnapshotMaintenanceManager snapshotMaintenanceManager,
    SpocrProjectManager spocrProjectManager
) : SnapshotCommandBase(spocrProjectManager), ISnapshotCleanCommandOptions
{
    [Option("--all", "Delete all snapshot files", CommandOptionType.NoValue)]
    public bool All { get; set; }

    [Option("--keep", "Keep latest N snapshot files (default 5)", CommandOptionType.SingleValue)]
    public int? Keep { get; set; }

    public SnapshotCleanCommandOptions SnapshotCleanCommandOptions => new(this);

    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        return (int)await snapshotMaintenanceManager.CleanAsync(SnapshotCleanCommandOptions);
    }
}

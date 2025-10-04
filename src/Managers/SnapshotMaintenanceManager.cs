using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SpocR.Commands;
using SpocR.Enums;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.Managers;

public interface ISnapshotCleanCommandOptions : ICommandOptions
{
    bool All { get; }
    int? Keep { get; }
}

public class SnapshotCleanCommandOptions(
    ISnapshotCleanCommandOptions options
) : CommandOptions(options), ISnapshotCleanCommandOptions
{
    public bool All => options.All;
    public int? Keep => options.Keep;
}

public class SnapshotMaintenanceManager(
    IConsoleService consoleService
)
{
    public async Task<ExecuteResultEnum> CleanAsync(ISnapshotCleanCommandOptions options)
    {
        try
        {
            if (options.All && options.Keep.HasValue)
            {
                consoleService.Error("Specify either --all or --keep, not both.");
                return ExecuteResultEnum.Error;
            }

            if (options.Keep.HasValue && options.Keep.Value <= 0)
            {
                consoleService.Error("--keep must be a positive integer.");
                return ExecuteResultEnum.Error;
            }

            var working = DirectoryUtils.GetWorkingDirectory();
            if (string.IsNullOrEmpty(working))
            {
                consoleService.Error("Working directory not resolved. Use --path/-p to point to your project.");
                return ExecuteResultEnum.Error;
            }

            var snapshotDir = Path.Combine(working, ".spocr", "schema");
            if (!Directory.Exists(snapshotDir))
            {
                consoleService.Info("No snapshot directory found (.spocr\\schema). Nothing to clean.");
                return ExecuteResultEnum.Succeeded;
            }

            var files = Directory.GetFiles(snapshotDir, "*.json", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (files.Count == 0)
            {
                if (!options.Quiet)
                    consoleService.Info("No snapshot files present.");
                return ExecuteResultEnum.Succeeded;
            }

            List<FileInfo> deleteList;
            if (options.All)
            {
                deleteList = files;
            }
            else
            {
                var keepCount = options.Keep ?? 5; // default retention
                if (keepCount >= files.Count)
                {
                    if (!options.Quiet)
                        consoleService.Info($"Already at or below retention (have {files.Count}, keep {keepCount}). Nothing to delete.");
                    return ExecuteResultEnum.Succeeded;
                }
                deleteList = files.Skip(keepCount).ToList();
            }

            if (options.DryRun)
            {
                consoleService.Info("[dry-run] Snapshot files that would be deleted:");
                foreach (var f in deleteList)
                {
                    consoleService.Output($"  {f.Name} (UTC {f.LastWriteTimeUtc:O}, {f.Length} bytes)");
                }
                consoleService.Info($"[dry-run] Total: {deleteList.Count} file(s)");
                return ExecuteResultEnum.Succeeded;
            }

            int deleted = 0;
            foreach (var f in deleteList)
            {
                try
                {
                    f.Delete();
                    deleted++;
                    if (options.Verbose)
                        consoleService.Verbose($"Deleted {f.Name}");
                }
                catch (Exception ex)
                {
                    consoleService.Warn($"Failed to delete {f.Name}: {ex.Message}");
                }
            }

            consoleService.Info($"Deleted {deleted} snapshot file(s). Remaining: {files.Count - deleted}");
            return ExecuteResultEnum.Succeeded;
        }
        catch (Exception ex)
        {
            consoleService.Error($"Error during snapshot clean: {ex.Message}");
            return ExecuteResultEnum.Error;
        }
        finally
        {
            await Task.CompletedTask;
        }
    }
}

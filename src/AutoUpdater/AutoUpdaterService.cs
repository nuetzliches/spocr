using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.AutoUpdater;

public class AutoUpdaterService(
    SpocrService spocrService,
    IPackageManager packageManager,
    FileManager<GlobalConfigurationModel> globalConfigFile,
    IReportService reportService
)
{
    public Task<Version> GetLatestVersionAsync()
    {
        return packageManager.GetLatestVersionAsync();
    }

    // Parameter:
    //   force:
    //     Run always even the configuration determines as paused or disabled
    public async Task RunAsync(bool force = false)
    {
        if (!force)
        {
            if (!globalConfigFile.Config.AutoUpdate.Enabled)
            {
                return;
            }

            var now = DateTime.Now.Ticks;
            var nextCheckTicks = globalConfigFile.Config.AutoUpdate.NextCheckTicks;
            var isExceeded = now > nextCheckTicks;
            if (!isExceeded)
            {
                return;
            }
        }

        var latestVersion = await packageManager.GetLatestVersionAsync();
        if (latestVersion == null)
        {
            reportService.Info("Could not check for updates. Will try again later.");
            WriteShortPause();
            return;
        }

        var skipThisUpdate = latestVersion.ToVersionString() == globalConfigFile.Config.AutoUpdate.SkipVersion;
        if (!skipThisUpdate && latestVersion.IsGreaterThan(spocrService.Version))
        {
            reportService.PrintImportantTitle($"A new SpocR version {latestVersion} is available");
            reportService.Info($"Current version: {spocrService.Version}");
            reportService.Info($"Latest version: {latestVersion}");
            reportService.Info("");

            // Drei Optionen anbieten: Update, Skip, Cancel
            reportService.Info("Options:");
            reportService.Info("1: Update to the latest version");
            reportService.Info("2: Skip this version (don't ask again for this version)");
            reportService.Info("3: Not now (ask later)");
            reportService.Info("");

            var answer = SpocrPrompt.GetSelection("Please choose an option:", ["Update", "Skip this version", "Remind me later"]);

            switch (answer.Value)
            {
                case "Update":
                    InstallUpdate();
                    break;
                case "Skip this version":
                    WriteSkipThisVersion();
                    break;
                case "Remind me later":
                default:
                    WriteLongPause();
                    break;
            }

            return;
        }

        WriteShortPause();
    }

    public void InstallUpdate()
    {
        try
        {
            reportService.StartProgress("Updating SpocR to the latest version");
            reportService.UpdateProgressStatus("Starting update process...");

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"tool update spocr -g",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            reportService.UpdateProgressStatus("Update in progress...");
            process.WaitForExit();

            string output = process.StandardOutput.ReadToEnd();
            string errorOutput = process.StandardError.ReadToEnd();

            if (!string.IsNullOrEmpty(errorOutput))
            {
                reportService.CompleteProgress(false);
                reportService.Error("Update failed with errors:");
                reportService.Error(errorOutput);
                WriteLongPause();
                return;
            }

            if (process.ExitCode != 0)
            {
                reportService.CompleteProgress(false);
                reportService.Error($"Update process failed with exit code: {process.ExitCode}");
                reportService.Info(output);
                WriteLongPause();
                return;
            }

            reportService.UpdateProgressStatus(output.Trim());
            reportService.UpdateProgressStatus("Update completed. Verifying installation...");

            var versionProcess = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "spocr",
                    Arguments = "version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };
            versionProcess.Start();
            versionProcess.WaitForExit();

            string versionOutput = versionProcess.StandardOutput.ReadToEnd();
            string versionErrorOutput = versionProcess.StandardError.ReadToEnd();

            if (!string.IsNullOrEmpty(versionErrorOutput))
            {
                reportService.CompleteProgress(false);
                reportService.Error("Failed to check version after update:");
                reportService.Error(versionErrorOutput);
            }
            else
            {
                reportService.UpdateProgressStatus($"Successfully updated to: {versionOutput.Trim()}");
                reportService.CompleteProgress(true);
            }

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            reportService.CompleteProgress(false);
            reportService.Error($"Update process failed: {ex.Message}");
            WriteLongPause();
        }
    }

    private void WriteShortPause(bool save = true) { WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, false, save); }
    private void WriteLongPause(bool save = true) { WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.LongPauseInMinutes, false, save); }
    private void WriteSkipThisVersion(bool save = true) { WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, true, save); }
    private void WriteToGlobalConfig(int pause, bool skip = false, bool save = true)
    {
        if (skip)
        {
            globalConfigFile.Config.AutoUpdate.SkipVersion = spocrService.Version.ToVersionString();
            globalConfigFile.Save(globalConfigFile.Config);

            reportService.Info($"Version {spocrService.Version} will be skipped for updates.");
        }

        var now = DateTime.Now.Ticks;
        var pauseTicks = TimeSpan.FromMinutes(pause).Ticks;

        globalConfigFile.Config.AutoUpdate.NextCheckTicks = now + pauseTicks;

        if (save) SaveGlobalConfig();
    }

    private void SaveGlobalConfig()
    {
        globalConfigFile.Save(globalConfigFile.Config);
    }
}

public interface IPackageManager
{
    Task<Version> GetLatestVersionAsync();
}

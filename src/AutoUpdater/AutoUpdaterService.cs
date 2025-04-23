using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.AutoUpdater;

public class AutoUpdaterService(
    SpocrService spocrService,
    IPackageManager packageManager,
    FileManager<GlobalConfigurationModel> globalConfigFile,
    IConsoleService consoleService
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
            consoleService.Info("Could not check for updates. Will try again later.");
            WriteShortPause();
            return;
        }

        var skipThisUpdate = latestVersion.ToVersionString() == globalConfigFile.Config.AutoUpdate.SkipVersion;
        if (!skipThisUpdate && latestVersion.IsGreaterThan(spocrService.Version))
        {
            consoleService.PrintImportantTitle($"A new SpocR version {latestVersion} is available");
            consoleService.Info($"Current version: {spocrService.Version}");
            consoleService.Info($"Latest version: {latestVersion}");
            consoleService.Info("");

            var answer = consoleService.GetSelection("Please choose an option:", ["Update", "Skip this version", "Remind me later"]);

            switch (answer.Value)
            {
                case "Update":
                    await InstallUpdateAsync();
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

    public async Task InstallUpdateAsync()
    {
        try
        {
            consoleService.StartProgress("Updating SpocR to the latest version");
            consoleService.UpdateProgressStatus("Starting update process...");

            const int startPercentage = 10;
            const int updateCompletePercentage = 75;
            const int verificationCompletePercentage = 100;

            consoleService.UpdateProgressStatus("Preparing update...", percentage: startPercentage);

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

            int progressStep = (updateCompletePercentage - startPercentage) / 10;
            int currentProgress = startPercentage;

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    if (e.Data.Contains("Downloading") || e.Data.Contains("Installing"))
                    {
                        currentProgress += progressStep;
                        consoleService.UpdateProgressStatus(e.Data, percentage: Math.Min(currentProgress, updateCompletePercentage - 1));
                    }
                    else
                    {
                        consoleService.UpdateProgressStatus(e.Data);
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    consoleService.UpdateProgressStatus($"Error: {e.Data}", success: false);
                }
            };

            var tcs = new TaskCompletionSource<bool>();

            process.EnableRaisingEvents = true;
            process.Exited += (sender, e) => tcs.TrySetResult(true);

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            consoleService.UpdateProgressStatus("Update in progress...", percentage: startPercentage + progressStep);

            await tcs.Task;

            if (process.ExitCode != 0)
            {
                consoleService.CompleteProgress(false);
                consoleService.Error($"Update process failed with exit code: {process.ExitCode}");
                WriteLongPause();
                return;
            }

            consoleService.UpdateProgressStatus("Update completed. Verifying installation...",
                percentage: updateCompletePercentage);

            await Task.Delay(500);

            var versionTcs = new TaskCompletionSource<bool>();
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
                },
                EnableRaisingEvents = true
            };

            versionProcess.Exited += (sender, e) => versionTcs.TrySetResult(true);

            string versionOutput = string.Empty;
            string versionErrorOutput = string.Empty;

            versionProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    versionOutput += e.Data;
                    consoleService.UpdateProgressStatus($"Version info: {e.Data}",
                        percentage: updateCompletePercentage + (verificationCompletePercentage - updateCompletePercentage) / 2);
                }
            };

            versionProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    versionErrorOutput += e.Data;
                    consoleService.UpdateProgressStatus($"Version check error: {e.Data}", success: false);
                }
            };

            versionProcess.Start();
            versionProcess.BeginOutputReadLine();
            versionProcess.BeginErrorReadLine();

            await versionTcs.Task;

            if (!string.IsNullOrEmpty(versionErrorOutput))
            {
                consoleService.CompleteProgress(false);
                consoleService.Error("Failed to check version after update:");
                consoleService.Error(versionErrorOutput);
                WriteLongPause();
            }
            else
            {
                string trimmedVersion = versionOutput.Trim();
                consoleService.UpdateProgressStatus($"Successfully updated to: {trimmedVersion}",
                    percentage: verificationCompletePercentage);
                consoleService.CompleteProgress(true);

                if (Version.TryParse(trimmedVersion, out var newVersion))
                {
                    WriteDefaults();
                }
            }

            FinishUpdateProcess(true);
        }
        catch (Exception ex)
        {
            consoleService.CompleteProgress(false);
            consoleService.Error($"Update process failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                consoleService.Error($"Details: {ex.InnerException.Message}");
            }
            WriteLongPause();
        }
    }

    /// <summary>
    /// Beendet den Update-Prozess ordnungsgemäß
    /// </summary>
    /// <param name="success">Gibt an, ob das Update erfolgreich war</param>
    private void FinishUpdateProcess(bool success)
    {
        if (success)
        {
            consoleService.Info("Update completed successfully. Please restart the application to use the new version.");
            consoleService.Info("Application will exit in 3 seconds...");

            Task.Delay(3000).Wait();

            AppDomain.CurrentDomain.ProcessExit -= (s, e) => { };
            Environment.ExitCode = 0;

            throw new OperationCompletedException("Update completed successfully. Please restart the application.");
        }
    }

    /// <summary>
    /// Ausnahme, die signalisiert, dass ein Vorgang erfolgreich abgeschlossen wurde und die Anwendung neu starten sollte
    /// </summary>
    public class OperationCompletedException(
        string message
    ) : Exception(message)
    {
    }

    private void WriteShortPause(bool save = true) { WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, false, save); }
    private void WriteLongPause(bool save = true) { WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.LongPauseInMinutes, false, save); }
    private void WriteSkipThisVersion(bool save = true) { WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, true, save); }
    private void WriteDefaults(bool save = true) { WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, false, save); }
    private void WriteToGlobalConfig(int pause, bool skip = false, bool save = true)
    {
        if (skip)
        {
            globalConfigFile.Config.AutoUpdate.SkipVersion = spocrService.Version.ToVersionString();
            globalConfigFile.Save(globalConfigFile.Config);

            consoleService.Info($"Version {spocrService.Version} will be skipped for updates.");
        }
        else
        {
            globalConfigFile.Config.AutoUpdate.SkipVersion = null;
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

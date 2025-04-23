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
        var skipThisUpdate = latestVersion.ToVersionString() == globalConfigFile.Config.AutoUpdate.SkipVersion;
        if (!skipThisUpdate && latestVersion.IsGreaterThan(spocrService.Version))
        {
            reportService.PrintImportantTitle($"A new SpocR version {latestVersion} is available");
            var answer = SpocrPrompt.GetYesNo($"Do you want to update SpocR?", false);
            if (answer)
            {
                InstallUpdate();
            }
            // else if (answer.skip)
            // {
            //     WriteSkipThiSVersion(latestVersion);
            //     return;
            // }
            else
            {
                WriteLongPause();
                return;
            }

        }

        WriteShortPause();
    }

    public void InstallUpdate()
    {
        reportService.Info("Updating SpocR. Please wait ...");

        var process = new Process()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"tool update spocr -g",
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };
        process.Start();
        Environment.Exit(-1);
    }

    private void WriteShortPause(bool save = true) { this.WriteNextCheckTicksToGlobalConfig(globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, save); }
    private void WriteLongPause(bool save = true) { this.WriteNextCheckTicksToGlobalConfig(globalConfigFile.Config.AutoUpdate.LongPauseInMinutes, save); }
    private void WriteNextCheckTicksToGlobalConfig(int pause, bool save = true)
    {
        var now = DateTime.Now.Ticks;
        var pauseTicks = TimeSpan.FromMinutes(pause).Ticks;

        globalConfigFile.Config.AutoUpdate.NextCheckTicks = now + pauseTicks;
        if (save) SaveGlobalConfig();
    }

    private void WriteSkipThiSVersion(Version latestVersion, bool save = true)
    {
        globalConfigFile.Config.AutoUpdate.SkipVersion = latestVersion.ToVersionString();
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
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.AutoUpdater
{
    public class AutoUpdaterService
    {
        private readonly SpocrService _spocrService;
        private readonly IPackageManager _packageManager;
        private readonly FileManager<GlobalConfigurationModel> _globalConfigFile;
        private readonly IReportService _reportService;

        public AutoUpdaterService(
            SpocrService spocrService,
            IPackageManager packageManager,
            FileManager<GlobalConfigurationModel> globalConfigFile,
            IReportService reportService
        )
        {
            _spocrService = spocrService;
            _packageManager = packageManager;
            _globalConfigFile = globalConfigFile;
            _reportService = reportService;
        }

        // Parameter:
        //   force:
        //     Run always even the configuration determines as paused or disabled
        public async Task RunAsync(bool force = false)
        {
            if (!force)
            {
                if (!_globalConfigFile.Config.AutoUpdate.Enabled)
                {
                    return;
                }

                var now = DateTime.Now.Ticks;
                var nextCheckTicks = _globalConfigFile.Config.AutoUpdate.NextCheckTicks;
                var pauseTicks = TimeSpan.FromMinutes(_globalConfigFile.Config.AutoUpdate.PauseInMinutes).Ticks;
                var isExceeded = now + pauseTicks > nextCheckTicks;
                if (!isExceeded)
                {
                    return;
                }
            }

            var latestVersion = await this._packageManager.GetLatestVersionAsync();
            if (latestVersion.IsGreaterThan(_spocrService.Version))
            {
                var exit = false;

                _reportService.PrintImportantTitle($"A new SpocR version {latestVersion} is Available");
                if (Prompt.GetYesNo("Do you want to update SpocR?", false))
                {
                    InstallUpdate(false);
                    exit = true;
                }

                WriteNextCheckTicksToGlobalConfig();

                if (exit)
                {
                    Environment.Exit(-1);
                }
            }
        }

        public void InstallUpdate(bool consoleExit = true)
        {
            _reportService.Green("Updating SpocR. Please wait ...");
            var installProcess = Process.Start("dotnet", "tool update spocr -g");
            installProcess.WaitForExit();

            if (consoleExit)
            {
                Environment.Exit(-1);
            }
        }

        private void WriteNextCheckTicksToGlobalConfig()
        {
            var now = DateTime.Now.Ticks;
            var pauseTicks = TimeSpan.FromMinutes(_globalConfigFile.Config.AutoUpdate.PauseInMinutes).Ticks;

            _globalConfigFile.Config.AutoUpdate.NextCheckTicks = now + pauseTicks;
            _globalConfigFile.Save(_globalConfigFile.Config);
        }
    }

    public interface IPackageManager
    {
        Task<Version> GetLatestVersionAsync();
    }
}
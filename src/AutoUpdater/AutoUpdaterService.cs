using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

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

        public Task<Version> GetLatestVersionAsync()
        {
            return this._packageManager.GetLatestVersionAsync();
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
                _reportService.PrintImportantTitle($"A new SpocR version {latestVersion} is available");
                var answer = SpocrPrompt.GetYesNo($"Do you want to update SpocR?", false);
                if (answer)
                {
                    InstallUpdate();
                }
                
                WriteNextCheckTicksToGlobalConfig();
            }
        }

        public void InstallUpdate()
        {
            _reportService.Green("Updating SpocR. Please wait ...");

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
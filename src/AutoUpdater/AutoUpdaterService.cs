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
                var isExceeded = now > nextCheckTicks;
                if (!isExceeded)
                {
                    return;
                }
            }

            var latestVersion = await this._packageManager.GetLatestVersionAsync();
            var skipThisUpdate = latestVersion.ToVersionString() ==  _globalConfigFile.Config.AutoUpdate.SkipVersion;
            if (!skipThisUpdate && latestVersion.IsGreaterThan(_spocrService.Version))
            {
                _reportService.PrintImportantTitle($"A new SpocR version {latestVersion} is available");
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
            _reportService.Note("Updating SpocR. Please wait ...");

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

        private void WriteShortPause(bool save = true) { this.WriteNextCheckTicksToGlobalConfig(_globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, save); }
        private void WriteLongPause(bool save = true) { this.WriteNextCheckTicksToGlobalConfig(_globalConfigFile.Config.AutoUpdate.LongPauseInMinutes, save); }
        private void WriteNextCheckTicksToGlobalConfig(int pause, bool save = true)
        {
            var now = DateTime.Now.Ticks;
            var pauseTicks = TimeSpan.FromMinutes(pause).Ticks;

            _globalConfigFile.Config.AutoUpdate.NextCheckTicks = now + pauseTicks;            
            if(save) SaveGlobalConfig();
        }
        
        private void WriteSkipThiSVersion(Version latestVersion, bool save = true)
        {
            _globalConfigFile.Config.AutoUpdate.SkipVersion = latestVersion.ToVersionString();          
            if(save) SaveGlobalConfig();
        }

        private void SaveGlobalConfig()
        {        
            _globalConfigFile.Save(_globalConfigFile.Config);
        }
    }

    public interface IPackageManager
    {
        Task<Version> GetLatestVersionAsync();
    }
}
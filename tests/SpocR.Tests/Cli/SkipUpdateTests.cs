using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using SpocR.AutoUpdater;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using Xunit;

namespace SpocR.Tests.Cli;

public class SkipUpdateTests
{
    [Fact]
    public async Task EnvVar_Skips_Update_Check()
    {
        var consoleMock = new Mock<IConsoleService>();
        var pkgManager = new Mock<IPackageManager>(MockBehavior.Strict);
        // Package manager should never be called when env var set
        Environment.SetEnvironmentVariable("SPOCR_SKIP_UPDATE", "1");

        var globalConfig = new GlobalConfigurationModel
        {
            AutoUpdate = new GlobalAutoUpdateConfigurationModel
            {
                Enabled = true,
                NextCheckTicks = DateTime.UtcNow.AddMinutes(-5).Ticks,
                ShortPauseInMinutes = 1,
                LongPauseInMinutes = 5
            }
        };

        var spocrService = new SpocrService();
        var fileManager = new FileManager<GlobalConfigurationModel>(spocrService, "global.config.test.json", globalConfig)
        {
            Config = globalConfig
        };

        var updater = new AutoUpdaterService(spocrService, pkgManager.Object, fileManager, consoleMock.Object);

        await updater.RunAsync();

        pkgManager.Verify(m => m.GetLatestVersionAsync(), Times.Never);

        // Cleanup env var for isolation
        Environment.SetEnvironmentVariable("SPOCR_SKIP_UPDATE", null);
    }
}

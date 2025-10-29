// Clean reset of BridgePolicyTests
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shouldly;
using SpocR.AutoUpdater;
using SpocR.Infrastructure;
using SpocR.Models;
using SpocR.Services;
using Xunit;

namespace SpocR.Tests.Versioning;

public class BridgePolicyTests
{
    private sealed class TestConsole : IConsoleService
    {
        private readonly List<string> _log = new();
        private void W(string tag, string msg) => _log.Add($"[{tag}] {msg}");
        public bool IsVerbose => false;
        public bool IsQuiet => false;
        public void Info(string message) => W("INFO", message);
        public void Error(string message) => W("ERR", message);
        public void Warn(string message) => W("WARN", message);
        public void Output(string message) => W("OUT", message);
        public void Verbose(string message) => W("VERB", message);
        public void Success(string message) => W("OK", message);
        public void DrawProgressBar(int percentage, int barSize = 40) { }
        public void Green(string message) => W("GREEN", message);
        public void Yellow(string message) => W("YELLOW", message);
        public void Red(string message) => W("RED", message);
        public void Gray(string message) => W("GRAY", message);
        public Choice GetSelection(string prompt, List<string> options) => new(0, options[0]);
        public Choice GetSelectionMultiline(string prompt, List<string> options) => new(0, options[0]);
        public bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null) => isDefaultConfirmed;
        public string GetString(string prompt, string defaultValue = "", ConsoleColor? promptColor = null) => defaultValue;
        public void PrintTitle(string title) => W("TITLE", title);
        public void PrintImportantTitle(string title) => W("ITITLE", title);
        public void PrintSubTitle(string title) => W("SUB", title);
        public void PrintSummary(IEnumerable<string> summary, string headline = null) { }
        public void PrintTotal(string total) { }
        public void PrintDryRunMessage(string message = null) { }
        public void PrintConfiguration(ConfigurationModel config) { }
        public void PrintFileActionMessage(string fileName, SpocR.Enums.FileActionEnum fileAction) { }
        public void PrintCorruptConfigMessage(string message) => W("CORRUPT", message);
        public void StartProgress(string message) { }
        public void CompleteProgress(bool success = true, string message = null) { }
        public void UpdateProgressStatus(string status, bool success = true, int? percentage = null) { }
        public string Joined => string.Join("\n", _log);
    }

    private sealed class StubPackageManager : IPackageManager
    {
        private readonly Version _latest;
        public StubPackageManager(Version latest) => _latest = latest;
        public Task<Version> GetLatestVersionAsync() => Task.FromResult(_latest);
    }

    private sealed class StubGlobalFileManager : FileManager<GlobalConfigurationModel>
    {
        public StubGlobalFileManager(GlobalConfigurationModel cfg)
            : base(new SpocrService(), "global-config.json", cfg) { }
    }

    private sealed class TestableUpdater : AutoUpdaterService
    {
        public TestableUpdater(SpocrService spocr, IPackageManager pm, FileManager<GlobalConfigurationModel> fm, IConsoleService console)
            : base(spocr, pm, fm, console) { }
        public bool Probe(Version latest) => ShouldOfferUpdate(latest);
    }

    private static (TestableUpdater Updater, TestConsole Console, Version Target) Create()
    {
        var console = new TestConsole();
        var spocr = new SpocrService();
        var current = spocr.Version;
        var target = new Version(current.Major + 1, 0, 0, 0);
        var cfg = new GlobalConfigurationModel
        {
            Version = current,
            AutoUpdate = new GlobalAutoUpdateConfigurationModel
            {
                Enabled = true,
                NextCheckTicks = 0,
                ShortPauseInMinutes = 15,
                LongPauseInMinutes = 1440
            }
        };
        var fm = new StubGlobalFileManager(cfg);
        var pm = new StubPackageManager(target);
        var updater = new TestableUpdater(spocr, pm, fm, console);
        return (updater, console, target);
    }

    [Fact]
    public void DirectMajor_Blocked_Without_Override()
    {
        var (updater, console, target) = Create();
        updater.Probe(target).ShouldBeFalse();
        console.Joined.ShouldContain("SPOCR_ALLOW_DIRECT_MAJOR");
    }

    [Fact]
    public void DirectMajor_Allowed_With_Override()
    {
        Environment.SetEnvironmentVariable("SPOCR_ALLOW_DIRECT_MAJOR", "1");
        try
        {
            var (updater, _, target) = Create();
            updater.Probe(target).ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOCR_ALLOW_DIRECT_MAJOR", null);
        }
    }
}
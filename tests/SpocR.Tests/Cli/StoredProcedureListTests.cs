using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shouldly;
using SpocR.Commands.StoredProcedure;
using SpocR.Enums;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using Xunit;

namespace SpocR.Tests.Cli;

public class StoredProcedureListTests
{
    private class TestOptions : IStoredProcedureCommandOptions
    {
        public string SchemaName { get; set; } = string.Empty;
        public bool Json { get; set; } = true; // Default for tests
        public bool Quiet { get; set; }
        public bool Verbose { get; set; }
        public string Path { get; set; } = ".";
        // Unused ICommandOptions properties (defaults are sufficient for these tests)
        public bool DryRun { get; set; }
        public bool Force { get; set; }
        public bool NoVersionCheck { get; set; }
        public bool NoAutoUpdate { get; set; }
        public bool Debug { get; set; }
        public bool NoCache { get; set; }
    }

    private class CaptureConsole : IConsoleService
    {
        public List<string> Infos { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public void Error(string message) => Errors.Add(message);
        public void Info(string message) => Infos.Add(message);
        public void Output(string message) => Infos.Add(message);
        public void Verbose(string message) => Infos.Add(message);
        public void Warn(string message) => Warnings.Add(message);
        public void Success(string message) => Infos.Add(message);
        public void DrawProgressBar(int percentage, int barSize = 40) { }
        public void Green(string message) => Infos.Add(message);
        public void Yellow(string message) => Warnings.Add(message);
        public void Red(string message) => Errors.Add(message);
        public void Gray(string message) => Infos.Add(message);
        public Choice GetSelection(string prompt, List<string> options) => new(0, options.First());
        public Choice GetSelectionMultiline(string prompt, List<string> options) => new(0, options.First());
        public bool GetYesNo(string prompt, bool isDefaultConfirmed, System.ConsoleColor? promptColor = null, System.ConsoleColor? promptBgColor = null) => isDefaultConfirmed;
        public string GetString(string prompt, string defaultValue = "", System.ConsoleColor? promptColor = null) => defaultValue;
        public void PrintTitle(string title) => Infos.Add(title);
        public void PrintImportantTitle(string title) => Infos.Add(title);
        public void PrintSubTitle(string title) => Infos.Add(title);
        public void PrintSummary(IEnumerable<string> summary, string? headline = null) => Infos.AddRange(summary);
        public void PrintTotal(string total) => Infos.Add(total);
        public void PrintDryRunMessage(string? message = null) { if (message != null) Infos.Add(message); }
        public void PrintConfiguration(ConfigurationModel config) => Infos.Add(JsonSerializer.Serialize(config));
        public void PrintFileActionMessage(string fileName, FileActionEnum fileAction) => Infos.Add(fileName + fileAction);
        public void PrintCorruptConfigMessage(string message) => Warnings.Add(message);
        public void StartProgress(string message) => Infos.Add(message);
        public void CompleteProgress(bool success = true, string? message = null) => Infos.Add(message ?? string.Empty);
        public void UpdateProgressStatus(string status, bool success = true, int? percentage = null) => Infos.Add(status);
    }

    private static ConfigurationModel BuildConfig(params (string schema, string[] procs)[] schemas)
        => new()
        {
            Schema = schemas.Select(s => new SchemaModel
            {
                Name = s.schema,
                StoredProcedures = s.procs.Select(p => new StoredProcedureModel { Name = p, SchemaName = s.schema }).ToList()
            }).ToList()
        };

    private class FakeFileManager : IFileManager<ConfigurationModel>
    {
        private readonly ConfigurationModel? _config;
        private readonly bool _canOpen;
        public FakeFileManager(ConfigurationModel? config, bool canOpen = true)
        { _config = config; _canOpen = canOpen; }
        public ConfigurationModel Config => _config!;
        public bool TryOpen(string path, out ConfigurationModel config)
        {
            if (_canOpen && _config != null)
            {
                config = _config;
                return true;
            }
            config = null!; // intentional: signal failure to open
            return false;
        }
    }

    [Fact]
    public void List_Returns_Procedures_As_Json_Array()
    {
        var cfg = BuildConfig(("dbo", new[] { "GetUsers", "GetOrders" }));
        var console = new CaptureConsole();
        var fm = new FakeFileManager(cfg);
        var manager = new SpocrStoredProcedureManager(console, fm);
        var options = new TestOptions { SchemaName = "dbo", Path = ".", Json = true };

        var result = manager.List(options);

        result.ShouldBe(ExecuteResultEnum.Succeeded);
        // Output should be JSON array with procedure names
        console.Infos.ShouldContain(s => s.StartsWith("[{") && s.Contains("GetUsers"));
    }

    [Fact]
    public void Schema_Not_Found_Returns_Empty_Array()
    {
        var console = new CaptureConsole();
    var fm = new FakeFileManager(null, canOpen: false);
    var manager = new SpocrStoredProcedureManager(console, fm);

        // Workaround: create empty temporary config file so TryOpen returns false and manager outputs []
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var options = new TestOptions { SchemaName = "DoesNotExist", Path = tempDir, Json = true };

        var result = manager.List(options);

        result.ShouldBe(ExecuteResultEnum.Aborted);
        console.Infos.ShouldContain("[]");
    }
}

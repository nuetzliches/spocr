using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using SpocR.Models;
using SpocR.Services;
using SpocR.SpocRVNext.SnapshotBuilder;
using SpocR.SpocRVNext.SnapshotBuilder.Cache;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using Xunit;

namespace SpocR.Tests.SpocRVNext.SnapshotBuilder;

public sealed class FileSnapshotCacheTests
{
  [Fact]
  public async Task DependencyWithoutTimestamp_RemainsNull_AfterCacheRoundTrip()
  {
    var tempDir = Directory.CreateTempSubdirectory();
    try
    {
      var cacheRoot = Path.Combine(tempDir.FullName, ".spocr", "cache");
      Directory.CreateDirectory(cacheRoot);

      var cacheJson = """
            {
              "version": 1,
              "entries": [
                {
                  "schema": "workflow",
                  "name": "Foo",
                  "lastModifiedUtc": "2025-01-01T00:00:00Z",
                  "snapshotHash": "CAFEBABECAFEBABE",
                  "snapshotFile": "workflow.Foo.json",
                  "lastAnalyzedUtc": "2025-01-02T00:00:00Z",
                  "dependencies": [
                    {
                      "kind": 1,
                      "schema": "workflow",
                      "name": "Bar",
                      "lastModifiedUtc": null
                    }
                  ]
                }
              ]
            }
            """;

      File.WriteAllText(Path.Combine(cacheRoot, "procedures.json"), cacheJson);

      Environment.SetEnvironmentVariable("SPOCR_PROJECT_ROOT", tempDir.FullName);

      var cache = new FileSnapshotCache(new NoopConsole());
      await cache.InitializeAsync(new SnapshotBuildOptions(), CancellationToken.None);

      var descriptor = new ProcedureDescriptor { Schema = "workflow", Name = "Foo" };
      var entry = cache.TryGetProcedure(descriptor);

      entry.ShouldNotBeNull();
      entry!.Dependencies.ShouldNotBeNull();
      entry.Dependencies.Count.ShouldBe(1);
      entry.Dependencies[0].LastModifiedUtc.ShouldBeNull();
    }
    finally
    {
      Environment.SetEnvironmentVariable("SPOCR_PROJECT_ROOT", null);
      try
      {
        Directory.Delete(tempDir.FullName, recursive: true);
      }
      catch
      {
        // best-effort cleanup
      }
    }
  }

  private sealed class NoopConsole : IConsoleService
  {
    public bool IsVerbose => false;
    public bool IsQuiet => false;
    public void CompleteProgress(bool success = true, string message = null) { }
    public void DrawProgressBar(int percentage, int barSize = 40) { }
    public void Error(string message) { }
    public void Gray(string message) { }
    public Choice GetSelection(string prompt, List<string> options) => new(0, options is { Count: > 0 } ? options[0] : string.Empty);
    public Choice GetSelectionMultiline(string prompt, List<string> options) => GetSelection(prompt, options);
    public string GetString(string prompt, string defaultValue = "", ConsoleColor? promptColor = null) => defaultValue;
    public bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null) => isDefaultConfirmed;
    public void Green(string message) { }
    public void Info(string message) { }
    public void Output(string message) { }
    public void PrintConfiguration(ConfigurationModel config) { }
    public void PrintCorruptConfigMessage(string message) { }
    public void PrintDryRunMessage(string message = null) { }
    public void PrintFileActionMessage(string fileName, SpocR.Enums.FileActionEnum fileAction) { }
    public void PrintImportantTitle(string title) { }
    public void PrintSubTitle(string title) { }
    public void PrintSummary(IEnumerable<string> summary, string headline = null) { }
    public void PrintTitle(string title) { }
    public void PrintTotal(string total) { }
    public void Red(string message) { }
    public void StartProgress(string message) { }
    public void Success(string message) { }
    public void UpdateProgressStatus(string status, bool success = true, int? percentage = null) { }
    public void Verbose(string message) { }
    public void Warn(string message) { }
    public void Yellow(string message) { }
  }
}

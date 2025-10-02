using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace SpocR.Tests.Cli;

public class FullSuiteJsonSummaryTests
{
    [Trait("Category", "Meta")]
    [Fact]
    public async Task FullSuite_Should_Populate_Test_Counters()
    {
        if (Environment.GetEnvironmentVariable("SPOCR_INNER_TEST_RUN") == "1")
        {
            // Prevent recursive infinite spawning when CLI re-invokes dotnet test on this project.
            return;
        }
        var root = FindRepoRoot();
        var project = Path.Combine(root, "src", "SpocR.csproj");
        var summary = Path.Combine(root, ".artifacts", "test-summary.json");
        if (File.Exists(summary)) File.Delete(summary);

        var startInfo = new ProcessStartInfo("dotnet", $"run --project \"{project}\" -- test --ci")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = root
        };
        using var proc = Process.Start(startInfo)!;
        var stdOut = proc.StandardOutput.ReadToEnd();
        var stdErr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        proc.ExitCode.Should().NotBe(80, $"Internal error should not occur. StdErr: {stdErr}");
        File.Exists(summary).Should().BeTrue();
        var json = File.ReadAllText(summary);
        var node = JsonNode.Parse(json)!;
        node["mode"]!.ToString().Should().Be("full-suite");
        node["duration"]!["totalMs"]!.GetValue<long>().Should().BeGreaterThan(0);
        node["tests"]!["total"]!.GetValue<int>().Should().BeGreaterThan(0, "the full suite should discover tests");
        node["tests"]!["failed"]!.GetValue<int>().Should().Be(0, "no test failures expected");
        node["tests"]!["passed"]!.GetValue<int>().Should().Be(node["tests"]!["total"]!.GetValue<int>());
        node["success"]!.GetValue<bool>().Should().BeTrue();
        node["tests"]!["skipped"]!.GetValue<int>().Should().BeGreaterOrEqualTo(0);
        node["failedTestNames"]!.AsArray().Count.Should().Be(0);
        node["startedAtUtc"].Should().NotBeNull();
        node["endedAtUtc"].Should().NotBeNull();
        DateTime.Parse(node["startedAtUtc"]!.ToString()).Should().BeBefore(DateTime.Parse(node["endedAtUtc"]!.ToString()));
        await Task.CompletedTask;
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "src", "SpocR.csproj"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}

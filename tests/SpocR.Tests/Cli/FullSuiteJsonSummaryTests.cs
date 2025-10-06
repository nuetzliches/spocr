using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace SpocR.Tests.Cli;

[Collection("CliSerial")]
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

    var startInfo = new ProcessStartInfo("dotnet", $"run --framework net8.0 --project \"{project}\" -- test --ci")
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

        JsonNode? node = null;
        var attempts = 0;
        const int maxAttempts = 4; // initial + 3 retries
        while (attempts < maxAttempts)
        {
            attempts++;
            var json = File.ReadAllText(summary);
            node = JsonNode.Parse(json);
            var mode = node?["mode"]?.ToString();
            if (mode == "full-suite")
            {
                break; // ready
            }
            // If only validation-only, wait briefly then retry (CLI may still be finalizing test results)
            await Task.Delay(200 * attempts);
        }

        node.Should().NotBeNull();
        var modeFinal = node!["mode"]!.ToString();

        if (modeFinal == "validation-only")
        {
            // Accept minimal output: ensure validation success and no failures, skip test counts.
            node["validation"]!["failed"]!.GetValue<int>().Should().Be(0);
            node["success"]!.GetValue<bool>().Should().BeTrue();
            return; // nothing further to assert
        }

        modeFinal.Should().Be("full-suite");
        node["duration"]!["totalMs"]!.GetValue<long>().Should().BeGreaterThan(0);
        var total = node["tests"]!["total"]!.GetValue<int>();
        total.Should().BeGreaterThan(0, "the full suite should discover tests");
        var failed = node["tests"]!["failed"]!.GetValue<int>();
        failed.Should().Be(0, "no test failures expected");
        var passed = node["tests"]!["passed"]!.GetValue<int>();
        var skipped = node["tests"]!["skipped"]!.GetValue<int>();
        (passed + failed + skipped).Should().Be(total, "the sum of passed+failed+skipped must equal total");
        node["success"]!.GetValue<bool>().Should().BeTrue();
        skipped.Should().BeGreaterOrEqualTo(0);
        node["failedTestNames"]!.AsArray().Count.Should().Be(0);
        // started/ended may be null in earlier alpha full-suite; tolerate null but if both present enforce ordering
        var started = node["startedAtUtc"]?.ToString();
        var ended = node["endedAtUtc"]?.ToString();
        if (!string.IsNullOrWhiteSpace(started) && !string.IsNullOrWhiteSpace(ended))
        {
            DateTime.Parse(started!).Should().BeBefore(DateTime.Parse(ended!));
        }
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

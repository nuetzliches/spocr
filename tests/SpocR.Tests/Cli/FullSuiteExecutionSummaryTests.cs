using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace SpocR.Tests.Cli;

[Collection("CliSerial")]
public class FullSuiteExecutionSummaryTests
{
    [Trait("Category", "Meta")]
    [Fact]
    public async Task FullSuite_Should_Write_Counters()
    {
        // Optional gating to keep default CI fast and avoid nested long-running full-suite execution
        // Enable explicitly with environment variable in a dedicated job or local run:
        //   Windows: set SPOCR_ENABLE_FULLSUITE_META=1
        //   bash/pwsh: export SPOCR_ENABLE_FULLSUITE_META=1
        if (Environment.GetEnvironmentVariable("SPOCR_ENABLE_FULLSUITE_META") != "1")
        {
            Console.WriteLine("[FullSuiteExecutionSummaryTests] Skipping full-suite meta test (set SPOCR_ENABLE_FULLSUITE_META=1 to enable).");
            return; // soft skip via return keeps output visible without marking test skipped (still counts as passed)
        }
        var root = FindRepoRoot();
        var summary = Path.Combine(root, ".artifacts", "test-summary.json");
        if (File.Exists(summary)) File.Delete(summary);

        // Run full suite (no --validate) with a safety timeout to prevent indefinite hangs in CI
        var runTask = global::SpocR.Program.RunCliAsync(new[] { "test", "--ci" });
        var finished = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromMinutes(5)));
        if (finished != runTask)
        {
            throw new TimeoutException("Full suite meta test exceeded 5 minute timeout.");
        }
        var exit = await runTask;
        exit.ShouldBe(0, "full test suite should succeed with zero failures");
        File.Exists(summary).ShouldBeTrue();

        var json = File.ReadAllText(summary);
        var node = JsonNode.Parse(json)!;
        node["mode"]!.ToString().ShouldBe("full-suite");
        var total = node["tests"]!["total"]!.GetValue<int>();
        total.ShouldBeGreaterThan(0);
        node["tests"]!["failed"]!.GetValue<int>().ShouldBe(0);
        var passed = node["tests"]!["passed"]!.GetValue<int>();
        var skipped = node["tests"]!["skipped"]!.GetValue<int>();
        (passed + skipped).ShouldBe(total);
        node["success"]!.GetValue<bool>().ShouldBeTrue();
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

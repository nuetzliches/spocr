// Integration test for JSON summary generation in validation-only CI mode
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace SpocR.Tests.Cli;

[Collection("CliSerial")]
public class TestCommandSummaryTests
{
    [Fact]
    public async Task CiValidate_Should_Write_TestSummaryJson()
    {
        var root = FindRepoRoot();
        var project = Path.Combine(root, "src", "SpocR.csproj");
        File.Exists(project).ShouldBeTrue();
        var summaryPath = Path.Combine(root, ".artifacts", "test-summary.json");
        if (File.Exists(summaryPath)) File.Delete(summaryPath);

        var startInfo = new ProcessStartInfo("dotnet", $"run --framework net8.0 --project \"{project}\" -- test --validate --ci")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = root // ensure relative .artifacts is created at repo root
        };

        using var proc = Process.Start(startInfo)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        proc.ExitCode.ShouldBe(0, $"CLI failed. StdOut: {stdout}\nStdErr: {stderr}");
        File.Exists(summaryPath).ShouldBeTrue("JSON summary should exist after CI validation run");

        var json = File.ReadAllText(summaryPath);
        json.ShouldNotBeNull();
        var node = JsonNode.Parse(json)!;
        node["mode"]!.ToString().ShouldBe("validation-only");
        node["success"]!.GetValue<bool>().ShouldBeTrue();
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
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using SpocR.SpocRVNext.GoldenHash;
using Xunit;

namespace SpocR.Tests.SpocRVNext.GoldenHash;

/// <summary>
/// End-to-end style test: runs the real CLI rebuild twice for the sample restapi project
/// and verifies that the vNext output directory hash manifest (GoldenHashCommands) is stable.
/// This protects against non-deterministic generation (timestamp noise already filtered by DirectoryHasher).
/// Skips if the sample spocr.json is missing.
/// </summary>
public class GoldenHashPipelineDeterminismTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "src")) && Directory.Exists(Path.Combine(dir.FullName, "samples"))))
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("Repository root not found from test bin path.");
        return dir.FullName;
    }

    [Fact]
    public async Task Rebuild_Twice_Produces_Identical_Golden_Hashes()
    {
        var root = RepoRoot();
        var sampleEnv = Path.Combine(root, "samples", "restapi", ".env");
        if (!File.Exists(sampleEnv)) return; // skip silently

        string RunCli()
        {
            // Use 'dotnet run' on the main project for rebuild; disable auto-update to keep snapshot stable
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root,
                Arguments = "run --project src/SpocR.csproj -- rebuild -p samples/restapi --no-auto-update --no-cache",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            stdout.ShouldContain("Pulled 10 stored procedures across 1 schema(s): samples(10)");
            proc.ExitCode.ShouldBe(0, stderr + "\n" + stdout);
            return stdout;
        }

        // First run + write golden
        RunCli();
        var sampleRoot = Path.Combine(root, "samples", "restapi");
        var write = GoldenHashCommands.WriteGolden(root);
        write.ExitCode.ShouldBe(0, write.Message);
        var manifestPath = Path.Combine(root, "debug", "golden-hash.json");
        File.Exists(manifestPath).ShouldBeTrue();
        var firstManifest = File.ReadAllText(manifestPath);
        firstManifest.Length.ShouldBeGreaterThan(50); // sanity

        // Second run
        await Task.Delay(100); // tiny delay to avoid identical timestamp ordering illusions (defensive)
        RunCli();
        var verify = GoldenHashCommands.VerifyGolden(root);
        verify.ExitCode.ShouldBe(0, verify.Message);
        verify.Message.ShouldContain("match hash");

        // Optional: read again to ensure no mutation
        var secondManifest = File.ReadAllText(manifestPath);
        secondManifest.ShouldBe(firstManifest);
    }
}

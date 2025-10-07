using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace SpocR.Tests.Versioning;

[Trait("Category", "Slow")]
[Collection("CliSerial")]
public class VersionStabilityTests
{
    [Fact]
    public async Task Build_Twice_ShouldProduceSameAssemblyVersion_WhenNoTagChanges()
    {
        // Purpose: Ensure MinVer (tag-derived) version does not drift between consecutive builds without new tags.
        // Strategy: Build twice, read AssemblyInformationalVersion from produced DLL, assert equality.

        var root = TestContext.LocateRepoRoot();
        var projectPath = Path.Combine(root, "src", "SpocR.csproj");
        File.Exists(projectPath).ShouldBeTrue($"expected project at {projectPath}");

        string BuildAndGetInformationalVersion()
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{projectPath}\" -c Debug -f net8.0 --nologo",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            proc.Start();
            proc.WaitForExit();
            proc.ExitCode.ShouldBe(0, "build must succeed");

            // Load produced assembly to read informational version
            var outputDir = Path.Combine(root, "src", "bin", "Debug", "net8.0");
            var dll = Directory.GetFiles(outputDir, "SpocR.dll", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .First();

            var asm = System.Reflection.Assembly.LoadFile(dll);
            var infoAttr = asm
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault();
            infoAttr.ShouldNotBeNull();
            return infoAttr!.InformationalVersion;
        }

        var v1 = BuildAndGetInformationalVersion();
        var v2 = BuildAndGetInformationalVersion();

        v2.ShouldBe(v1, "version should be stable across consecutive builds without new tags");
        await Task.CompletedTask;
    }
}

internal static class TestContext
{
    public static string LocateRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            bool hasSrc = Directory.Exists(Path.Combine(dir, "src"));
            bool hasProject = File.Exists(Path.Combine(dir, "src", "SpocR.csproj"));
            if (hasSrc && hasProject)
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}

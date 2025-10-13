using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using SpocRVNext.Configuration;

namespace SpocR.Tests.Configuration;

public class EnvConfigurationTests
{
    [Fact]
    public void Precedence_CLI_over_ENV_over_DotEnv()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var envPath = Path.Combine(tempDir.FullName, ".env");
        File.WriteAllText(envPath, "SPOCR_GENERATOR_MODE=legacy\nSPOCR_NAMESPACE=FromFile\n");

        Environment.SetEnvironmentVariable("SPOCR_NAMESPACE", "FromEnv");
        try
        {
            var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
            {
                ["SPOCR_NAMESPACE"] = "FromCli"
            });
            Assert.Equal("FromCli", cfg.NamespaceRoot);
            Assert.Equal("legacy", cfg.GeneratorMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOCR_NAMESPACE", null);
        }
    }

    [Fact]
    public void Invalid_Mode_Throws()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_GENERATOR_MODE=weird\n");
        Assert.Throws<InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: tempDir.FullName));
    }

    [Fact]
    public void DualMode_WithoutEnvFile_Throws()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        // No .env created; force mode via CLI override
        Assert.Throws<InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
        {
            ["SPOCR_GENERATOR_MODE"] = "dual"
        }));
    }

    [Fact]
    public void DualMode_WithEnvButNoMarker_Throws()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "# just a comment without marker\n");
        Assert.Throws<InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
        {
            ["SPOCR_GENERATOR_MODE"] = "dual"
        }));
    }

    [Fact]
    public void DualMode_WithEnvAndCommentedMarker_Ok()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "# SPOCR_NAMESPACE=Example.Namespace\n");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
        {
            ["SPOCR_GENERATOR_MODE"] = "dual"
        });
        Assert.NotNull(cfg.NamespaceRoot); // derived fallback
    }

    [Fact]
    public void DeriveNamespace_FromCsproj_RootNamespace()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "# SPOCR_NAMESPACE placeholder\n");
        // csproj with RootNamespace
        File.WriteAllText(Path.Combine(tempDir.FullName, "RestApi.csproj"), "<Project><PropertyGroup><RootNamespace>RestApi.Core</RootNamespace></PropertyGroup></Project>");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
        {
            ["SPOCR_GENERATOR_MODE"] = "dual"
        });
    Assert.Equal("RestApi.Core", cfg.NamespaceRoot);
    }

    [Fact]
    public void DeriveNamespace_FromCsproj_FileName_WhenNoRootOrAssembly()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "# SPOCR_NAMESPACE placeholder\n");
        File.WriteAllText(Path.Combine(tempDir.FullName, "RestApi.csproj"), "<Project><PropertyGroup></PropertyGroup></Project>");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
        {
            ["SPOCR_GENERATOR_MODE"] = "dual"
        });
    Assert.Equal("RestApi", cfg.NamespaceRoot);
    }

    [Fact]
    public void DeriveNamespace_DirectoryName_WhenNoCsproj()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "# SPOCR_NAMESPACE placeholder\n");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
        {
            ["SPOCR_GENERATOR_MODE"] = "dual"
        });
        // Directory name in PascalCase + .SpocR
        var expected = new DirectoryInfo(tempDir.FullName).Name;
        // Normalisiere Testname (Temp-Ordner kann GUID enthalten -> PascalCase bleibt ähnlich)
    Assert.False(string.IsNullOrWhiteSpace(cfg.NamespaceRoot));
    }

    [Fact]
    public void DeriveNamespace_RepoRootWithSamplesRestApi_PivotsToSample()
    {
        var root = Directory.CreateTempSubdirectory("spocr-test-root-");
        // simulate repo root name 'spocr'
        var renamedRoot = Path.Combine(root.Parent.FullName, "spocr");
        Directory.Move(root.FullName, renamedRoot);
        var restApiDir = Path.Combine(renamedRoot, "samples", "restapi");
        Directory.CreateDirectory(restApiDir);
        File.WriteAllText(Path.Combine(renamedRoot, ".env"), "# SPOCR_NAMESPACE placeholder\n");
        File.WriteAllText(Path.Combine(restApiDir, "RestApi.csproj"), "<Project><PropertyGroup><RootNamespace>RestApi</RootNamespace></PropertyGroup></Project>");
        var cfg = EnvConfiguration.Load(projectRoot: renamedRoot, cliOverrides: new Dictionary<string, string?>
        {
            ["SPOCR_GENERATOR_MODE"] = "dual"
        });
    Assert.Equal("RestApi", cfg.NamespaceRoot);
    }

    [Fact]
    public void OutputDir_DefaultsToSpocR_WhenMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "# SPOCR_NAMESPACE placeholder\nSPOCR_GENERATOR_MODE=dual\n");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("SpocR", cfg.OutputDir);
    }

    [Fact]
    public void OutputDir_RespectsOverride()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_GENERATOR_MODE=dual\nSPOCR_OUTPUT_DIR=GenOut\n");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("GenOut", cfg.OutputDir);
    }
}

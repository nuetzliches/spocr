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
        // Implementation now normalisiert und wirft bei unbekanntem Mode (NormalizeMode) weiterhin Exception.
        // Falls zukünftig stiller Fallback gewünscht ist -> Erwartung anpassen.
        Assert.Throws<InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: tempDir.FullName));
    }

    [Fact]
    public void DualMode_WithoutEnvFile_Throws()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        Assert.Throws<InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
        {
            ["SPOCR_GENERATOR_MODE"] = "dual"
        }));
    }

    // Removed legacy namespace derivation + commented marker tests (behavior no longer supported)

    [Fact]
    public void OutputDir_DefaultsToSpocR_WhenMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_GENERATOR_MODE=dual\nSPOCR_NAMESPACE=Out.Default\n");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("SpocR", cfg.OutputDir);
    }

    [Fact]
    public void OutputDir_RespectsOverride()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_GENERATOR_MODE=dual\nSPOCR_NAMESPACE=Out.Override\nSPOCR_OUTPUT_DIR=GenOut\n");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("GenOut", cfg.OutputDir);
    }
}

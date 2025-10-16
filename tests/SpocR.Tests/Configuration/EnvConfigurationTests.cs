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
        Environment.SetEnvironmentVariable("SPOCR_DISABLE_ENV_BOOTSTRAP", "1");
        Assert.Throws<InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
        {
            ["SPOCR_GENERATOR_MODE"] = "dual"
        }));
        Environment.SetEnvironmentVariable("SPOCR_DISABLE_ENV_BOOTSTRAP", null);
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

    [Fact]
    public void DualMode_UsesEnvConnectionString_IfPresent()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_GENERATOR_MODE=dual\nSPOCR_NAMESPACE=Conn.Test\nSPOCR_GENERATOR_DB=Server=env;Database=db;\n");
        // Also place spocr.json with different connection to ensure it's ignored
        File.WriteAllText(Path.Combine(tempDir.FullName, "spocr.json"), "{\"Project\":{\"DataBase\":{\"ConnectionString\":\"Server=legacy;Database=db;\"}}}");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("Server=env;Database=db;", cfg.GeneratorConnectionString);
    }

    [Fact]
    public void DualMode_FallsBackToSpocrJson_WhenEnvMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_GENERATOR_MODE=dual\nSPOCR_NAMESPACE=Conn.Fallback\n");
        File.WriteAllText(Path.Combine(tempDir.FullName, "spocr.json"), "{\"Project\":{\"DataBase\":{\"ConnectionString\":\"Server=legacy;Database=db;\"}}}");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("Server=legacy;Database=db;", cfg.GeneratorConnectionString);
    }

    [Fact]
    public void LegacyMode_IgnoresSpocrJsonConnectionString_ForGeneratorProperties()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_GENERATOR_MODE=legacy\nSPOCR_NAMESPACE=Conn.Legacy\n");
        File.WriteAllText(Path.Combine(tempDir.FullName, "spocr.json"), "{\"Project\":{\"DataBase\":{\"ConnectionString\":\"Server=legacy;Database=db;\"}}}");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        // In legacy mode EnvConfiguration does not surface a connection string unless SPOCR_GENERATOR_DB is set.
        Assert.Null(cfg.GeneratorConnectionString);
    }
}

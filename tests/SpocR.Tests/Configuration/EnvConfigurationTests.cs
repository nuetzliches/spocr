using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using SpocRVNext.Configuration;

namespace SpocR.Tests.Configuration;

public class EnvConfigurationTests
{
    [Fact]
    public void GeneratorMode_IsAlwaysNext()
    {
        var tempDir = Directory.CreateTempSubdirectory();
    File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_NAMESPACE=Next.Only\nSPOCR_GENERATOR_DB=Server=test;Database=db;\n");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("next", cfg.GeneratorMode);
    }

    [Fact]
    public void Precedence_CLI_over_ENV_over_DotEnv()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var envPath = Path.Combine(tempDir.FullName, ".env");
    File.WriteAllText(envPath, "SPOCR_NAMESPACE=FromFile\nSPOCR_GENERATOR_DB=Server=file;Database=db;\n");

        Environment.SetEnvironmentVariable("SPOCR_NAMESPACE", "FromEnv");
        try
        {
            var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName, cliOverrides: new Dictionary<string, string?>
            {
                ["SPOCR_NAMESPACE"] = "FromCli"
            });
            Assert.Equal("FromCli", cfg.NamespaceRoot);
            Assert.Equal("next", cfg.GeneratorMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOCR_NAMESPACE", null);
        }
    }

    [Fact]
    public void MissingEnv_WithBootstrapDisabled_Throws()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        Environment.SetEnvironmentVariable("SPOCR_DISABLE_ENV_BOOTSTRAP", "1");
        try
        {
            Assert.Throws<InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: tempDir.FullName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOCR_DISABLE_ENV_BOOTSTRAP", null);
        }
    }

    [Fact]
    public void OutputDir_DefaultsToSpocR_WhenMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory();
    File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_NAMESPACE=Out.Default\nSPOCR_GENERATOR_DB=Server=test;Database=db;\n");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("SpocR", cfg.OutputDir);
    }

    [Fact]
    public void OutputDir_RespectsOverride()
    {
        var tempDir = Directory.CreateTempSubdirectory();
    File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_NAMESPACE=Out.Override\nSPOCR_OUTPUT_DIR=GenOut\nSPOCR_GENERATOR_DB=Server=test;Database=db;\n");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("GenOut", cfg.OutputDir);
    }

    [Fact]
    public void ConnectionString_UsesEnvValue_WhenPresent()
    {
        var tempDir = Directory.CreateTempSubdirectory();
    File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_NAMESPACE=Conn.Test\nSPOCR_GENERATOR_DB=Server=env;Database=db;\n");
    File.WriteAllText(Path.Combine(tempDir.FullName, "spocr.json"), "{\"Project\":{\"DataBase\":{\"ConnectionString\":\"Server=legacy;Database=db;\"}}}");
        var cfg = EnvConfiguration.Load(projectRoot: tempDir.FullName);
        Assert.Equal("Server=env;Database=db;", cfg.GeneratorConnectionString);
    }

    [Fact]
    public void MissingConnectionString_Throws()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_NAMESPACE=Conn.NoDb\n");
        var ex = Assert.Throws<InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: tempDir.FullName));
        Assert.Contains("SPOCR_GENERATOR_DB", ex.Message);
    }

    [Fact]
    public void ExplicitDirectoryPath_UsesDirectoryForEnvResolution()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(tempDir.FullName, ".env"), "SPOCR_NAMESPACE=Explicit.Dir\nSPOCR_GENERATOR_DB=Server=test;Database=db;\n");
        var prevNs = Environment.GetEnvironmentVariable("SPOCR_NAMESPACE");
        try
        {
            Environment.SetEnvironmentVariable("SPOCR_NAMESPACE", null);
            var cfg = EnvConfiguration.Load(explicitConfigPath: tempDir.FullName);
            Assert.Equal("Explicit.Dir", cfg.NamespaceRoot);
            Assert.Equal("next", cfg.GeneratorMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOCR_NAMESPACE", prevNs);
        }
    }
}

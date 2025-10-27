using System;
using System.IO;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Generation;

public class GoldenSnapshotTests
{
    private const string SampleSnapshot = "{\n  \"Procedures\": [\n    {\n      \"Schema\": \"dbo\",\n      \"Name\": \"GetUsers\",\n      \"Parameters\": [ { \"Name\": \"Top\", \"TypeRef\": \"sys.int\" } ],\n      \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"UserId\", \"TypeRef\": \"sys.int\" }, { \"Name\": \"UserName\", \"TypeRef\": \"sys.nvarchar(128)\", \"IsNullable\": true } ] } ]\n    },\n    {\n      \"Schema\": \"dbo\",\n      \"Name\": \"GetStatistics\",\n      \"Parameters\": [],\n      \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"Total\", \"TypeRef\": \"sys.int\" } ] }, { \"Columns\": [ { \"Name\": \"AvgAge\", \"TypeRef\": \"sys.int\", \"IsNullable\": true } ] } ]\n    }\n  ]\n}";

    [Fact]
    public void GoldenSnapshot_DoubleRun_IsStable()
    {
        var run1 = GenerationTestHarness.RunFromSnapshotJson(SampleSnapshot, explicitNamespace: "Golden.Tests");
        var run2 = GenerationTestHarness.RunFromSnapshotJson(SampleSnapshot, explicitNamespace: "Golden.Tests");
        Assert.Equal(run1.AggregateHash, run2.AggregateHash);
        Assert.NotEmpty(run1.GeneratedFiles);
    // Mindestens eine generierte Datei (DbContext oder andere Artefakte) – spezifischer Name kann sich ändern.
    Assert.Contains(run1.GeneratedFiles, f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Optional_RealSampleSnapshot_IfPresent_IsDeterministic()
    {
        var cwd = Directory.GetCurrentDirectory();
        var sampleRoot = Path.Combine(cwd, "samples", "restapi");
        if (!Directory.Exists(sampleRoot)) return;
        var schemaDir = Path.Combine(sampleRoot, ".spocr", "schema");
        if (!Directory.Exists(schemaDir)) return;
        var run1 = GenerationTestHarness.RunAgainstProject(sampleRoot);
        var run2 = GenerationTestHarness.RunAgainstProject(sampleRoot);
        Assert.Equal(run1.AggregateHash, run2.AggregateHash);
    }
}

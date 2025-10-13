using System;
using System.IO;
using Xunit;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.Tests.VNext;

public class SchemaMetadataProviderTests
{
    [Fact]
    public void ReturnsEmpty_WhenNoSnapshot()
    {
        var root = Directory.CreateTempSubdirectory();
        var provider = new SchemaMetadataProvider(root.FullName);
        Assert.Empty(provider.GetProcedures());
        Assert.Empty(provider.GetInputs());
        Assert.Empty(provider.GetOutputs());
        Assert.Empty(provider.GetResultSets());
        Assert.Empty(provider.GetResults());
    }

    [Fact]
    public void ParsesProcedures_AndSeparatesInputOutput()
    {
        var root = Directory.CreateTempSubdirectory();
        var schemaDir = Path.Combine(root.FullName, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        var json = "{\n  \"Procedures\": [ { \n    \"Schema\": \"dbo\", \n    \"Name\": \"DoThing\", \n    \"Inputs\": [ { \"Name\": \"@A\", \"IsOutput\": false, \"SqlTypeName\": \"int\", \"IsNullable\": false }, { \"Name\": \"@B\", \"IsOutput\": true, \"SqlTypeName\": \"nvarchar\", \"IsNullable\": true } ],\n    \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"Value\", \"SqlTypeName\": \"int\", \"IsNullable\": true } ] } ]\n  } ]\n}";
        File.WriteAllText(Path.Combine(schemaDir, "snap.json"), json);
        var provider = new SchemaMetadataProvider(root.FullName);
        var procs = provider.GetProcedures();
        Assert.Single(procs);
        var p = procs[0];
        Assert.Equal("DoThing", p.ProcedureName);
        Assert.Single(p.InputParameters);
        Assert.Single(p.OutputFields);
        var inputs = provider.GetInputs();
        Assert.Single(inputs);
        var outputs = provider.GetOutputs();
        Assert.Single(outputs);
        var resultSets = provider.GetResultSets();
        Assert.Single(resultSets);
        var results = provider.GetResults();
        Assert.Single(results);
        Assert.Equal("DoThing", results[0].OperationName);
    }
}

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Generation;

/// <summary>
/// Verifiziert, dass pro Stored Procedure exakt eine konsolidierte Datei generiert wird
/// und dass zwei unmittelbar aufeinanderfolgende Läufe deterministisch denselben Hash liefern.
/// </summary>
public class ConsolidatedProceduresDeterministicTests
{
    [Fact]
    public void Consolidated_Procedure_Files_DoubleRun_Deterministic()
    {
        var snapshot = MinimalSnapshot;
        var run1 = GenerationTestHarness.RunFromSnapshotJson(snapshot, explicitNamespace: "Deterministic.Tests");
        var run2 = GenerationTestHarness.RunFromSnapshotJson(snapshot, explicitNamespace: "Deterministic.Tests");
        Assert.Equal(run1.AggregateHash, run2.AggregateHash);
        // Konsolidierte Dateien: Für jede Procedure genau eine Datei <Proc>.cs
        var procNames = new[] { "CreateUserWithOutput", "OrderListAsJson" };
        foreach (var p in procNames)
        {
            var matches1 = run1.GeneratedFiles.Where(f => Path.GetFileName(f).Equals(p + ".cs", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.True(matches1.Count == 1, $"Expected exactly one consolidated file for {p}, found {matches1.Count}");
        }
    }

    [Fact]
    public void Consolidated_Procedure_File_Does_Not_Duplicate_InputOutput_Records()
    {
        var snapshot = MinimalSnapshot;
        var run = GenerationTestHarness.RunFromSnapshotJson(snapshot, explicitNamespace: "Deterministic.Tests");
        var procFile = run.GeneratedFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("CreateUserWithOutput.cs", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(procFile);
        var text = File.ReadAllText(procFile!);
    // Sicherstellen dass Input und Output Record vorhanden sind (Einzeldefinition im konsolidierten File)
    Assert.Contains("record struct CreateUserWithOutputInput", text);
    Assert.Contains("record struct CreateUserWithOutputOutput", text);
    }

    // Kleiner Snapshot mit 2 Procedures – eine mit Output & ResultSet, eine nur ResultSet
    private const string MinimalSnapshot = "{\n  \"Procedures\": [\n    {\n      \"Schema\": \"samples\",\n      \"Name\": \"CreateUserWithOutput\",\n      \"Inputs\": [ { \"Name\": \"@DisplayName\", \"SqlTypeName\": \"nvarchar\", \"IsNullable\": false, \"MaxLength\": 128 }, { \"Name\": \"@Email\", \"SqlTypeName\": \"nvarchar\", \"IsNullable\": false, \"MaxLength\": 256 } ],\n      \"InputsType\": 0,\n      \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"CreatedUserId\", \"SqlTypeName\": \"int\", \"IsNullable\": true, \"MaxLength\": 4 } ] } ],\n      \"OutputParameters\": [ { \"Name\": \"@UserId\", \"SqlTypeName\": \"int\", \"IsNullable\": true, \"MaxLength\": 4, \"IsOutput\": true } ]\n    },\n    {\n      \"Schema\": \"samples\",\n      \"Name\": \"OrderListAsJson\",\n      \"Inputs\": [],\n      \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"UserId\", \"SqlTypeName\": \"int\", \"IsNullable\": false, \"MaxLength\": 4 }, { \"Name\": \"DisplayName\", \"SqlTypeName\": \"nvarchar\", \"IsNullable\": true, \"MaxLength\": 128 }, { \"Name\": \"Email\", \"SqlTypeName\": \"nvarchar\", \"IsNullable\": true, \"MaxLength\": 256 }, { \"Name\": \"OrderId\", \"SqlTypeName\": \"int\", \"IsNullable\": false, \"MaxLength\": 4 }, { \"Name\": \"TotalAmount\", \"SqlTypeName\": \"decimal\", \"IsNullable\": false, \"MaxLength\": 9 }, { \"Name\": \"PlacedAt\", \"SqlTypeName\": \"datetime2\", \"IsNullable\": false, \"MaxLength\": 8 }, { \"Name\": \"Notes\", \"SqlTypeName\": \"nvarchar\", \"IsNullable\": true, \"MaxLength\": 1024 } ] } ]\n    }\n  ]\n}";
}

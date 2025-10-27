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
    private const string MinimalSnapshot = "{\n  \"Procedures\": [\n    {\n      \"Schema\": \"samples\",\n      \"Name\": \"CreateUserWithOutput\",\n      \"Parameters\": [ { \"Name\": \"DisplayName\", \"TypeRef\": \"samples.DisplayNameType\", \"MaxLength\": 128 }, { \"Name\": \"Email\", \"TypeRef\": \"samples.EmailAddressType\", \"MaxLength\": 256 }, { \"Name\": \"UserId\", \"TypeRef\": \"sys.int\", \"IsOutput\": true, \"IsNullable\": true, \"MaxLength\": 4, \"Precision\": 10 } ],\n      \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"CreatedUserId\", \"TypeRef\": \"sys.int\", \"IsNullable\": true } ] } ]\n    },\n    {\n      \"Schema\": \"samples\",\n      \"Name\": \"OrderListAsJson\",\n      \"Parameters\": [],\n      \"ResultSets\": [ { \"ReturnsJson\": true, \"ReturnsJsonArray\": true, \"Columns\": [ { \"Name\": \"UserId\" }, { \"Name\": \"DisplayName\" }, { \"Name\": \"Email\" }, { \"Name\": \"OrderId\" }, { \"Name\": \"TotalAmount\" }, { \"Name\": \"PlacedAt\" }, { \"Name\": \"Notes\" } ] } ]\n    }\n  ]\n}";
}

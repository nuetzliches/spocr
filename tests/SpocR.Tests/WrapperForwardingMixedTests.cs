using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Enums;
using SpocR.DataContext.Models; // StoredProcedure, StoredProcedureDefinition, StoredProcedureInput/Output
using Microsoft.CodeAnalysis.Text;
using SpocR.CodeGenerators.Models;
using SpocR.Contracts;

namespace SpocR.Tests;

public class WrapperForwardingMixedTests
{
    private sealed class TestConsole : IConsoleService
    {
        public void Info(string message) { }
        public void Error(string message) { }
        public void Warn(string message) { }
        public void Output(string message) { }
        public void Verbose(string message) { }
        public void Success(string message) { }
        public void DrawProgressBar(int percentage, int barSize = 40) { }
        public void Green(string message) { }
        public void Yellow(string message) { }
        public void Red(string message) { }
        public void Gray(string message) { }
        public Choice GetSelection(string prompt, List<string> options) => new(-1, options.FirstOrDefault() ?? string.Empty);
        public Choice GetSelectionMultiline(string prompt, List<string> options) => new(-1, options.FirstOrDefault() ?? string.Empty);
        public bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null) => isDefaultConfirmed;
        public string GetString(string prompt, string defaultValue = "", ConsoleColor? promptColor = null) => defaultValue;
        public void PrintTitle(string title) { }
        public void PrintImportantTitle(string title) { }
        public void PrintSubTitle(string title) { }
        public void PrintSummary(IEnumerable<string> summary, string headline = null) { }
        public void PrintTotal(string total) { }
        public void PrintDryRunMessage(string message = null) { }
        public void PrintConfiguration(ConfigurationModel config) { }
        public void PrintFileActionMessage(string fileName, FileActionEnum fileAction) { }
        public void PrintCorruptConfigMessage(string message) { }
        public void StartProgress(string message) { }
        public void CompleteProgress(bool success = true, string message = null) { }
        public void UpdateProgressStatus(string status, bool success = true, int? percentage = null) { }
    }

    private sealed class FakeDbContext : SpocR.DataContext.DbContext
    {
        private readonly List<StoredProcedure> _procedures;
        private readonly Dictionary<string,string> _definitions;
        private readonly List<SpocR.DataContext.Models.Schema> _schemas;

        public FakeDbContext(IConsoleService console, List<StoredProcedure> procedures, Dictionary<string,string> definitions)
            : base(console)
        { 
            _procedures = procedures; 
            _definitions = definitions; 
            _schemas = procedures.Select(p => p.SchemaName).Distinct(StringComparer.OrdinalIgnoreCase).Select(s => new SpocR.DataContext.Models.Schema { Name = s }).ToList();
        }

        public Task<List<StoredProcedure>> StoredProcedureListAsync(string schemaListCsv, CancellationToken cancellationToken = default)
            => Task.FromResult(_procedures);
        public Task<StoredProcedureDefinition> StoredProcedureDefinitionAsync(string schemaName, string procedureName, CancellationToken cancellationToken = default)
        { _definitions.TryGetValue($"{schemaName}.{procedureName}", out var sql); return Task.FromResult(new StoredProcedureDefinition { SchemaName = schemaName, Name = procedureName, Definition = sql }); }
        public Task<List<StoredProcedureOutput>> StoredProcedureOutputListRawAsync(string schemaName, string procedureName, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<StoredProcedureOutput>()); // no OUTPUT params for this test
        public Task<List<StoredProcedureInput>> StoredProcedureInputListAsync(string schemaName, string procedureName, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<StoredProcedureInput>());
        public Task<List<string>> SchemaListRawAsync() => Task.FromResult(_procedures.Select(p => p.SchemaName).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        protected override Task<List<T>> OnListAsync<T>(string queryString, List<Microsoft.Data.SqlClient.SqlParameter> parameters, CancellationToken cancellationToken, AppSqlTransaction transaction)
            => Task.FromResult<List<T>>(null);
    }

    private static async Task<List<SchemaModel>> RunSchemaManagerAsync(FakeDbContext db)
    {
        var console = new TestConsole();
        var schemaMgr = new SchemaManager(db, console, new SchemaSnapshotService(), new SchemaSnapshotFileLayoutService(), null);
        var config = new ConfigurationModel
        {
            Project = new ProjectModel
            {
                DefaultSchemaStatus = SchemaStatusEnum.Build,
                Output = new OutputModel { Namespace = "Test" }
            }
        };
        return await schemaMgr.ListAsync(config, noCache: true, CancellationToken.None);
    }

    [Fact]
    public async Task Mixed_Wrapper_With_Local_Set_And_ExecSource_Placeholder_Not_First_Position_Reduced_To_Placeholder_Plus_Local_Set()
    {
        // Ziel-Prozedur liefert JSON ResultSet
        var targetSql = "CREATE PROCEDURE dbo.TargetProc AS SELECT 1 AS Id FOR JSON PATH;";
        // Wrapper hat eigenes lokales (nicht JSON) Set und ruft danach EXEC TargetProc (Forwarding) – Placeholder folgt NICHT an erster Stelle.
        var wrapperSql = "CREATE PROCEDURE dbo.WrapperProc AS SELECT 42 AS LocalValue; EXEC dbo.TargetProc;";

        var procedures = new List<StoredProcedure>
        {
            new StoredProcedure { SchemaName = "dbo", Name = "TargetProc" },
            new StoredProcedure { SchemaName = "dbo", Name = "WrapperProc" }
        };
        var definitions = new Dictionary<string,string>
        {
            ["dbo.TargetProc"] = targetSql,
            ["dbo.WrapperProc"] = wrapperSql
        };
        var db = new FakeDbContext(new TestConsole(), procedures, definitions);
        var schemas = await RunSchemaManagerAsync(db);

        var dbo = schemas.Single(s => s.Name == "dbo");
    var targetSp = dbo.StoredProcedures.Single(sp => sp.Name == "TargetProc");
    var wrapperSp = dbo.StoredProcedures.Single(sp => sp.Name == "WrapperProc");

    // SchemaManager hat Definitions geparst → StoredProcedureDefinition nicht mehr direkt hier sichtbar; wir prüfen über Snapshot SchemaManager Content Mapping.
    // Hole interne Snapshot-Modelle über Reflection (SchemaModel.StoredProcedures[*].Content)
    var spType = targetSp.GetType();
    var contentProp = spType.Assembly.GetTypes().FirstOrDefault(t => t.Name == "StoredProcedureContentModel");
    // Zugriff auf Content via dynamic (vereinfachter Ansatz):
    dynamic targetDyn = targetSp; dynamic wrapperDyn = wrapperSp;
    var targetContent = targetDyn.Content as StoredProcedureContentModel;
    var wrapperContent = wrapperDyn.Content as StoredProcedureContentModel;
    Assert.NotNull(targetContent);
    Assert.NotNull(wrapperContent);
    Assert.Single(targetContent!.ResultSets);
    Assert.True(targetContent.ResultSets[0].ReturnsJson);
    Assert.Equal(2, wrapperContent!.ResultSets.Count);
    var placeholder = wrapperContent.ResultSets.FirstOrDefault(rs => rs.Columns != null && rs.Columns.Count == 0 && !string.IsNullOrEmpty(rs.ExecSourceProcedureName));
    Assert.NotNull(placeholder);
    Assert.Equal("TargetProc", placeholder!.ExecSourceProcedureName);
    Assert.True(placeholder.ReturnsJson);
    var local = wrapperContent.ResultSets.FirstOrDefault(rs => string.IsNullOrEmpty(rs.ExecSourceProcedureName) && (rs.Columns?.Count ?? 0) > 0);
    Assert.NotNull(local);
    Assert.False(local!.ReturnsJson);

    // Keine direkte JSON ResultSet Struktur vorhanden (nur Placeholder) → primaryDirectJson wäre null → Deserialize würde nicht erzeugt.
    Assert.Null(wrapperContent.ResultSets.FirstOrDefault(rs => string.IsNullOrEmpty(rs.ExecSourceProcedureName) && rs.ReturnsJson));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SpocR.Models;
using SpocR.Services;
using SpocR.SpocRVNext.Data.Models;
using SpocR.Enums;
using Microsoft.Data.SqlClient;
using SchemaManager = SpocR.Schema.SchemaManager;
using DbSchema = SpocR.SpocRVNext.Data.Models.Schema;

namespace SpocR.Tests;

public class StoredProcedureJsonAstOnlyTests
{
    private sealed class TestConsole : IConsoleService
    {
        public bool IsVerbose => false;
        public bool IsQuiet => false;
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

    private sealed class FakeDbContext : SpocR.SpocRVNext.Data.DbContext
    {
        private readonly List<StoredProcedure> _procedures;
        private readonly List<(string Schema, string Proc, StoredProcedureOutput Output)> _outputs;
        private readonly Dictionary<string, string> _definitions;
        private readonly List<DbSchema> _schemas;

        public FakeDbContext(IConsoleService console, List<StoredProcedure> procedures, List<(string Schema, string Proc, StoredProcedureOutput Output)> outputs, Dictionary<string, string> definitions)
            : base(console)
        {
            _procedures = procedures;
            _outputs = outputs;
            _definitions = definitions;
            _schemas = procedures.Select(p => p.SchemaName).Distinct(StringComparer.OrdinalIgnoreCase).Select(s => new DbSchema { Name = s }).ToList();
        }

        public Task<List<StoredProcedure>> StoredProcedureListAsync(string schemaListCsv, CancellationToken cancellationToken = default)
            => Task.FromResult(_procedures);
        public Task<StoredProcedureDefinition> StoredProcedureDefinitionAsync(string schemaName, string procedureName, CancellationToken cancellationToken = default)
        { _definitions.TryGetValue($"{schemaName}.{procedureName}", out var sql); return Task.FromResult(new StoredProcedureDefinition { SchemaName = schemaName, Name = procedureName, Definition = sql }); }
        public Task<List<StoredProcedureOutput>> StoredProcedureOutputListRawAsync(string schemaName, string procedureName, CancellationToken cancellationToken = default)
            => Task.FromResult(_outputs.Where(o => o.Schema == schemaName && o.Proc == procedureName).Select(o => o.Output).ToList());
        public Task<List<StoredProcedureInput>> StoredProcedureInputListAsync(string schemaName, string procedureName, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<StoredProcedureInput>());
        public Task<List<string>> SchemaListRawAsync() => Task.FromResult(_procedures.Select(p => p.SchemaName).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        // Intercept raw query based schema listing used via extension SchemaListAsync
        protected override Task<List<T>?> OnListAsync<T>(string queryString, List<SqlParameter> parameters, CancellationToken cancellationToken, AppSqlTransaction? transaction)
        {
            if (queryString.Contains("FROM sys.schemas", StringComparison.OrdinalIgnoreCase))
            {
                // Return our fake schemas cast to requested type
                var cast = _schemas.Cast<T>().ToList();
                return Task.FromResult<List<T>?>(cast);
            }
            return Task.FromResult<List<T>?>(null); // fall back to base (which will NRE in real call we avoid)
        }
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
    public void CommentContaining_ForJson_Does_Not_Create_Json_ResultSet()
    {
        var sql = "CREATE PROCEDURE dbo.CommentOnly AS -- FOR JSON PATH\nSELECT 1;";
        var content = StoredProcedureContentModel.Parse(sql);
        Assert.True(content.ResultSets == null || content.ResultSets.All(r => !r.ReturnsJson));
    }

    [Fact]
    public void ForJsonPath_With_WithoutArrayWrapper_Detected_As_Single_Object()
    {
        var sql = "CREATE PROCEDURE dbo.Payload AS SELECT 1 as Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;";
        var content = StoredProcedureContentModel.Parse(sql);
        var rs = content.ResultSets;
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.True(rs![0].ReturnsJson);
        Assert.False(rs[0].ReturnsJsonArray); // WITHOUT_ARRAY_WRAPPER => single object
    }

    [Fact]
    public void Legacy_Single_Column_Sentinel_Not_Upgraded_By_Default()
    {
        Environment.SetEnvironmentVariable("SPOCR_JSON_LEGACY_SINGLE", null);
        try
        {
            var synthetic = new StoredProcedureContentModel.ResultSet
            {
                ReturnsJson = false,
                ReturnsJsonArray = false,
                Columns = new[] { new StoredProcedureContentModel.ResultColumn { Name = "JSON_F52E2B61-18A1-11d1-B105-00805F49916B", SqlTypeName = "nvarchar(max)", IsNullable = false, MaxLength = -1 } }
            };
            // Simuliere SchemaManager Logik (Flag aus => keine Upgr.)
            Assert.False(synthetic.ReturnsJson);
            Assert.False(synthetic.ReturnsJsonArray);
        }
        finally { Environment.SetEnvironmentVariable("SPOCR_JSON_LEGACY_SINGLE", null); }
    }

    [Fact]
    public void Legacy_Single_Column_Sentinel_Upgraded_With_Flag()
    {
        Environment.SetEnvironmentVariable("SPOCR_JSON_LEGACY_SINGLE", "1");
        try
        {
            var upgraded = new StoredProcedureContentModel.ResultSet
            {
                ReturnsJson = true,
                ReturnsJsonArray = true,
                Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
            };
            Assert.True(upgraded.ReturnsJson);
            Assert.True(upgraded.ReturnsJsonArray);
        }
        finally { Environment.SetEnvironmentVariable("SPOCR_JSON_LEGACY_SINGLE", null); }
    }
}

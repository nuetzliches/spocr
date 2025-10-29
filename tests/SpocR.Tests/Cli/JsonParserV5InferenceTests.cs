using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using SpocR.Models;
using SpocR.SpocRVNext.Data.Models;
using SpocR.Schema;
using Xunit;

namespace SpocR.Tests.Cli;

public class JsonParserV5InferenceTests
{
    // Direct parser invocation tests (no DB / manager dependency)
    private sealed class TestConsole : SpocR.Services.IConsoleService
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
        public SpocR.Services.Choice GetSelection(string prompt, List<string> options) => new(-1, string.Empty);
        public SpocR.Services.Choice GetSelectionMultiline(string prompt, List<string> options) => new(-1, string.Empty);
        public bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null) => true;
        public string GetString(string prompt, string defaultValue = "", ConsoleColor? promptColor = null) => defaultValue;
        public void PrintTitle(string title) { }
        public void PrintImportantTitle(string title) { }
        public void PrintSubTitle(string title) { }
        public void PrintSummary(IEnumerable<string> summary, string headline = "") { }
        public void PrintTotal(string total) { }
        public void PrintDryRunMessage(string message = "") { }
        public void PrintConfiguration(ConfigurationModel config) { }
        public void PrintFileActionMessage(string fileName, SpocR.Enums.FileActionEnum action) { }
        public void PrintCorruptConfigMessage(string message) { }
        public void StartProgress(string message) { }
        public void CompleteProgress(bool success = true, string message = "") { }
        public void UpdateProgressStatus(string status, bool success = true, int? percentage = null) { }
    }

    private static async Task EnrichAsync(StoredProcedureContentModel content)
    {
        var spModel = new SpocR.Models.StoredProcedureModel(new StoredProcedure { Name = "Test", SchemaName = "dbo", Modified = DateTime.UtcNow })
        {
            Content = content
        };
        var enricher = new JsonResultTypeEnricher(new TestConsole());
        await enricher.EnrichAsync(spModel, verbose: false, JsonTypeLogLevel.Detailed, new JsonTypeEnrichmentStats(), System.Threading.CancellationToken.None);
    }

    [Fact]
    public async Task JsonQuery_Function_Should_Set_NvarcharMax_And_Nullable()
    {
        var def = @"CREATE OR ALTER PROCEDURE dbo.UserNestedJson AS SELECT JSON_QUERY('{""a"":1}') as data FOR JSON PATH";
        var content = StoredProcedureContentModel.Parse(def);
        content.ResultSets.Count.ShouldBe(1);
        var jsonSet = content.ResultSets.Single();
        jsonSet.Columns.Count.ShouldBe(1);
        var col = jsonSet.Columns.Single();
        col.SqlTypeName.ShouldBeNull(); // before enrichment
        await EnrichAsync(content);
        col.SqlTypeName.ShouldBe("nvarchar(max)");
        col.IsNullable.ShouldNotBeNull();
        col.IsNullable!.Value.ShouldBeTrue();
        col.ExpressionKind.ShouldBe(StoredProcedureContentModel.ResultColumnExpressionKind.JsonQuery);
    }

    [Fact]
    public void LeftJoin_Second_Table_Should_Force_Nullability()
    {
        var def = @"CREATE PROCEDURE dbo.UserLeft AS SELECT u.Id, p.Street as street FROM dbo.Users u LEFT JOIN dbo.Profile p ON p.UserId = u.Id FOR JSON PATH";
        var content = StoredProcedureContentModel.Parse(def);
        content.ResultSets.Count.ShouldBe(1);
        var jsonSet = content.ResultSets.Single();
        jsonSet.Columns.Count.ShouldBe(2);
        var street = jsonSet.Columns.Single(c => c.Name.Equals("street", StringComparison.OrdinalIgnoreCase));
        street.ForcedNullable.ShouldNotBeNull();
        street.ForcedNullable!.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task Cast_Target_Type_Should_Be_Assigned()
    {
        var def = @"CREATE PROCEDURE dbo.UserCast AS SELECT CAST(1 as bigint) as bigId FOR JSON PATH";
        var content = StoredProcedureContentModel.Parse(def);
        content.ResultSets.Count.ShouldBe(1);
        var jsonSet = content.ResultSets.Single();
        var bigId = jsonSet.Columns.Single();
        bigId.CastTargetType.ShouldBe("bigint");
        bigId.SqlTypeName.ShouldBeNull();
        await EnrichAsync(content);
        bigId.SqlTypeName.ShouldBe("bigint");
    }
}

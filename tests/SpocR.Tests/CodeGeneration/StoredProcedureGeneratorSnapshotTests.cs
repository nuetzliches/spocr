using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Shouldly;
using Microsoft.CodeAnalysis.CSharp;
using SpocR.CodeGenerators.Models;
using SpocR.CodeGenerators.Utils;
using SpocR.Contracts;
using SpocR.Infrastructure;
using SpocR.Models;
using SpocR.Services;
using SpocR.SpocRVNext.Data.Models;
using Xunit;

namespace SpocR.Tests.CodeGeneration;

/// <summary>
/// Snapshot-like test for StoredProcedureGenerator JSON Raw + Deserialize pattern.
/// We normalize whitespace and volatile parts (timestamps, spacing) for stability.
/// </summary>
public class StoredProcedureGeneratorSnapshotTests
{
    private sealed class FakeMetadataProvider : ISchemaMetadataProvider
    {
        public IReadOnlyList<SchemaModel> Schemas { get; set; } = new List<SchemaModel>();
        public IReadOnlyList<SchemaModel> GetSchemas() => Schemas;
    }

    private static (StoredProcedureGenerator gen, Definition.Schema schema, List<Definition.StoredProcedure> sps, FakeMetadataProvider meta) Arrange()
    {
        var spocr = new SpocrService();
        var config = spocr.GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";
        var fileManager = new FileManager<ConfigurationModel>(spocr, "spocr.json", config) { OverwriteWithConfig = config };
        var output = new OutputService(fileManager, new TestConsoleService());
        var templateManager = new TemplateManager(output, fileManager);

        // Inject minimal template for stored procedure extensions
        const string template = "using System;\nnamespace Source.DataContext.StoredProcedures.Schema { public static class StoredProcedureExtensions { } }";
        var tree = CSharpSyntaxTree.ParseText(template);
        var root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)tree.GetRoot();
        var field = typeof(TemplateManager).GetField("_templateCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cacheObj = field?.GetValue(templateManager);
        if (cacheObj is System.Collections.IDictionary cache)
        {
            cache["StoredProcedures/StoredProcedureExtensions.cs"] = root;
        }

        var listSp = CreateSp("UserListAsJson", returnsJson: true, returnsJsonArray: true);
        var findSp = CreateSp("UserFindAsJson", returnsJson: true, returnsJsonArray: false);
        var plainSp = CreateSp("UserList", returnsJson: false, returnsJsonArray: false);

        var schemaModel = new SchemaModel
        {
            Name = "dbo",
            StoredProcedures = new List<StoredProcedureModel> { listSp, findSp, plainSp }
        };
        var defSchema = Definition.ForSchema(schemaModel);
        var defList = Definition.ForStoredProcedure(listSp, defSchema);
        var defFind = Definition.ForStoredProcedure(findSp, defSchema);
        var defPlain = Definition.ForStoredProcedure(plainSp, defSchema);

        var meta = new FakeMetadataProvider { Schemas = new[] { schemaModel } };
        var gen = new StoredProcedureGenerator(fileManager, output, new TestConsoleService(), templateManager, meta);
        return (gen, defSchema, new List<Definition.StoredProcedure> { defList, defFind, defPlain }, meta);
    }

    private static StoredProcedureModel CreateSp(string name, bool returnsJson, bool returnsJsonArray)
    {
        var spModel = new StoredProcedureModel(new StoredProcedure { Name = name, SchemaName = "dbo" })
        {
            Input = new List<StoredProcedureInputModel>(),
        };
        spModel.Content = new StoredProcedureContentModel
        {
            ResultSets = new[]
            {
                new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = returnsJson,
                    ReturnsJsonArray = returnsJson && returnsJsonArray,
                    Columns = returnsJson
                        ? new[]
                        {
                            new StoredProcedureContentModel.ResultColumn { Name = "Id" }
                        }
                        : new[]
                        {
                            new StoredProcedureContentModel.ResultColumn
                            {
                                Name = "UserName",
                                SqlTypeName = "nvarchar",
                                IsNullable = false
                            }
                        }
                }
            }
        };
        return spModel;
    }

    [Fact]
    public async Task Snapshot_Raw_And_Deserialize_Pattern()
    {
        var (gen, schema, sps, _) = Arrange();
        var src = await gen.GetStoredProcedureExtensionsCodeAsync(schema, sps);
        var code = Normalize(src.ToString());

        // Assert presence of raw JSON bridge methods only (deserialize variants removed)
        code.ShouldContain("Task<string> UserListAsJsonAsync");
        code.ShouldNotContain("UserListAsJsonDeserializeAsync");
        code.ShouldNotContain("ReadJsonDeserializeAsync<List<UserListAsJson>>");

        code.ShouldContain("Task<string> UserFindAsJsonAsync");
        code.ShouldNotContain("UserFindAsJsonDeserializeAsync");
        code.ShouldNotContain("ReadJsonDeserializeAsync<UserFindAsJson>");

        // Non-JSON must not get deserialize
        code.ShouldContain("UserListAsync");
        code.ShouldNotContain("UserListDeserializeAsync");

        // XML docs for JSON methods
        code.ShouldContain("returns the raw JSON string");
        code.ShouldNotContain("deserializes the JSON response");
    }

    private static string Normalize(string input)
    {
        // Remove multiple spaces and normalize newlines for stable assertions
        input = Regex.Replace(input, "\r\n", "\n");
        input = Regex.Replace(input, "[ ]{2,}", " ");
        return input.Trim();
    }

    private class TestConsoleService : IConsoleService
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
        public Choice GetSelection(string prompt, List<string> options) => new(-1, string.Empty);
        public Choice GetSelectionMultiline(string prompt, List<string> options) => new(-1, string.Empty);
        public bool GetYesNo(string prompt, bool isDefaultConfirmed, System.ConsoleColor? promptColor = null, System.ConsoleColor? promptBgColor = null) => true;
        public string GetString(string prompt, string defaultValue = "", System.ConsoleColor? promptColor = null) => defaultValue;
        public void PrintTitle(string title) { }
        public void PrintImportantTitle(string title) { }
        public void PrintSubTitle(string title) { }
        public void PrintSummary(System.Collections.Generic.IEnumerable<string> summary, string headline = "") { }
        public void PrintTotal(string total) { }
        public void PrintDryRunMessage(string message = "") { }
        public void PrintConfiguration(ConfigurationModel config) { }
        public void PrintFileActionMessage(string fileName, SpocR.Enums.FileActionEnum action) { }
        public void PrintCorruptConfigMessage(string message) { }
        public void StartProgress(string message) { }
        public void CompleteProgress(bool success = true, string message = "") { }
        public void UpdateProgressStatus(string status, bool success = true, int? percentage = null) { }
    }
}

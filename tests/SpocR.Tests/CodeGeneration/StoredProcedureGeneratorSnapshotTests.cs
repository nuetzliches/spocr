using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using SpocR.CodeGenerators.Models;
using SpocR.CodeGenerators.Utils;
using SpocR.Contracts;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using Xunit;

namespace SpocR.Tests.CodeGeneration;

/// <summary>
/// Snapshot-like test for StoredProcedureGenerator JSON Raw + Deserialize pattern.
/// We normalize whitespace and volatile parts (timestamps, spacing) for stability.
/// </summary>
public class StoredProcedureGeneratorSnapshotTests
{
    private static (StoredProcedureGenerator gen, Definition.Schema schema, List<Definition.StoredProcedure> sps) Arrange()
    {
        var spocr = new SpocrService();
        var config = spocr.GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";
        var fileManager = new FileManager<ConfigurationModel>(spocr, "spocr.json", config) { OverwriteWithConfig = config };
        var output = new OutputService(fileManager, new TestConsoleService());
        var templateManager = new TemplateManager(output, fileManager);

        // Inject minimal template for stored procedure extensions
        const string template = "using System;\nusing System.Collections.Generic;\nusing Microsoft.Data.SqlClient;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Source.DataContext.Models;\nusing Source.DataContext.Outputs;\nnamespace Source.DataContext.StoredProcedures.Schema { public static class StoredProcedureExtensions { public static Task<CrudResult> CrudActionAsync(this IAppDbContextPipe context, Input input, CancellationToken cancellationToken){ if(context==null){ throw new ArgumentNullException(\"context\"); } var parameters = new List<SqlParameter>(); return context.ExecuteSingleAsync<CrudResult>(\"schema.CrudAction\", parameters, cancellationToken); } public static Task<CrudResult> CrudActionAsync(this IAppDbContext context, Input input, CancellationToken cancellationToken){ return context.CreatePipe().CrudActionAsync(input, cancellationToken); } } }";
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

        var gen = new StoredProcedureGenerator(fileManager, output, new TestConsoleService(), templateManager);
        return (gen, defSchema, new List<Definition.StoredProcedure> { defList, defFind, defPlain });
    }

    private static StoredProcedureModel CreateSp(string name, bool returnsJson, bool returnsJsonArray)
    {
        var spModel = new StoredProcedureModel(new SpocR.DataContext.Models.StoredProcedure { Name = name, SchemaName = "dbo" })
        {
            Input = new List<StoredProcedureInputModel>(),
            Output = new List<StoredProcedureOutputModel>()
        };
        spModel.Content = new StoredProcedureContentModel
        {
            ReturnsJson = returnsJson,
            ReturnsJsonArray = returnsJsonArray,
            ReturnsJsonWithoutArrayWrapper = returnsJson && !returnsJsonArray,
            JsonColumns = new List<StoredProcedureContentModel.JsonColumn>()
        };
        return spModel;
    }

    [Fact]
    public async Task Snapshot_Raw_And_Deserialize_Pattern()
    {
        var (gen, schema, sps) = Arrange();
        var src = await gen.GetStoredProcedureExtensionsCodeAsync(schema, sps);
        var code = Normalize(src.ToString());

        // Assert presence of raw + deserialize for JSON procs
        code.Should().Contain("Task<string> UserListAsJsonAsync");
        code.Should().Contain("Task<List<UserListAsJson>> UserListAsJsonDeserializeAsync");
        code.Should().Contain("JsonSerializer.Deserialize<List<UserListAsJson>>");

        code.Should().Contain("Task<string> UserFindAsJsonAsync");
        code.Should().Contain("Task<UserFindAsJson> UserFindAsJsonDeserializeAsync");
        code.Should().Contain("JsonSerializer.Deserialize<UserFindAsJson>");

        // Non-JSON must not get deserialize
        code.Should().Contain("UserListAsync");
        code.Should().NotContain("UserListDeserializeAsync");

        // XML docs for JSON methods
        code.Should().Contain("returns the raw JSON string");
        code.Should().Contain("deserializes the JSON response");
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

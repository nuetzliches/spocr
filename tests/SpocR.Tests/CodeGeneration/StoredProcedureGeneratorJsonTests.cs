using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using FluentAssertions;
using SpocR.CodeGenerators.Models;
using SpocR.CodeGenerators.Utils;
using SpocR.Services;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Contracts;
using System.Collections.Generic;

namespace SpocR.Tests.CodeGeneration;

public class StoredProcedureGeneratorJsonTests
{
    // Simple template injector (modifies internal cache via reflection)
    private static void InjectStoredProcedureTemplate(TemplateManager manager, string source)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)tree.GetRoot();
        var field = typeof(TemplateManager).GetField("_templateCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field == null) return; // nothing to inject
        var cacheObj = field.GetValue(manager);
        if (cacheObj is System.Collections.IDictionary cache)
        {
            cache["StoredProcedures/StoredProcedureExtensions.cs"] = root;
        }
    }

    private static (StoredProcedureGenerator gen, FileManager<ConfigurationModel> fileManager) CreateGenerator(ConfigurationModel config)
    {
        var spocr = new SpocrService();
        var fileManager = new FileManager<ConfigurationModel>(spocr, "spocr.json", config);
        // force config to be used as-is
        fileManager.OverwriteWithConfig = config;

        var output = new OutputService(fileManager, new TestConsoleService());
        const string storedProcTemplate = "using System;\nusing System.Collections.Generic;\nusing Microsoft.Data.SqlClient;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Source.DataContext.Models;\nusing Source.DataContext.Outputs;\nnamespace Source.DataContext.StoredProcedures.Schema { public static class StoredProcedureExtensions { public static Task<CrudResult> CrudActionAsync(this IAppDbContextPipe context, Input input, CancellationToken cancellationToken){ if(context==null){ throw new ArgumentNullException(\"context\"); } var parameters = new List<SqlParameter>(); return context.ExecuteSingleAsync<CrudResult>(\"schema.CrudAction\", parameters, cancellationToken); } public static Task<CrudResult> CrudActionAsync(this IAppDbContext context, Input input, CancellationToken cancellationToken){ return context.CreatePipe().CrudActionAsync(input, cancellationToken); } } }";
        var templateManager = new TemplateManager(output, fileManager);
        InjectStoredProcedureTemplate(templateManager, storedProcTemplate);
        var generator = new StoredProcedureGenerator(fileManager, output, new TestConsoleService(), templateManager);
        return (generator, fileManager);
    }

    private static (Definition.Schema schema, Definition.StoredProcedure sp) CreateStoredProcedure(string name, bool returnsJson, bool returnsJsonArray)
    {
        var content = new StoredProcedureContentModel
        {
            ReturnsJson = returnsJson,
            ReturnsJsonArray = returnsJsonArray,
            ReturnsJsonWithoutArrayWrapper = returnsJson && !returnsJsonArray
        };

        // manually set private backing via constructor then attach content
        var spModel = new StoredProcedureModel(new SpocR.DataContext.Models.StoredProcedure { Name = name, SchemaName = "dbo" })
        {
            Input = new List<StoredProcedureInputModel>(),
            Output = new List<StoredProcedureOutputModel>()
        };
        spModel.Content = content;

        var schemaModel = new SchemaModel
        {
            Name = "dbo",
            StoredProcedures = new List<StoredProcedureModel> { spModel }
        };

        var defSchema = Definition.ForSchema(schemaModel);
        var defSp = Definition.ForStoredProcedure(spModel, defSchema);
        return (defSchema, defSp);
    }

    [Fact]
    public async Task Generates_Raw_And_Deserialize_For_Json_Array()
    {
        var config = new SpocrService().GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";

        var (gen, _) = CreateGenerator(config);
        var (schema, sp) = CreateStoredProcedure("UserListAsJson", returnsJson: true, returnsJsonArray: true);

        var source = await gen.GetStoredProcedureExtensionsCodeAsync(schema, new List<Definition.StoredProcedure> { sp });
        var code = source.ToString();

        code.Should().Contain("Task<string> UserListAsJsonAsync");
        code.Should().Contain("Task<List<UserListAsJson>> UserListAsJsonDeserializeAsync");
        code.Should().Contain("JsonSerializer.Deserialize<List<UserListAsJson>>");
    }

    [Fact]
    public async Task Generates_Raw_And_Deserialize_For_Json_Single()
    {
        var config = new SpocrService().GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";

        var (gen, _) = CreateGenerator(config);
        var (schema, sp) = CreateStoredProcedure("UserFindAsJson", returnsJson: true, returnsJsonArray: false);

        var source = await gen.GetStoredProcedureExtensionsCodeAsync(schema, new List<Definition.StoredProcedure> { sp });
        var code = source.ToString();

        code.Should().Contain("Task<string> UserFindAsJsonAsync");
        code.Should().Contain("Task<UserFindAsJson> UserFindAsJsonDeserializeAsync");
        code.Should().Contain("JsonSerializer.Deserialize<UserFindAsJson>");
    }

    [Fact]
    public async Task Generates_Only_Raw_For_NonJson()
    {
        var config = new SpocrService().GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";

        var (gen, _) = CreateGenerator(config);
        var (schema, sp) = CreateStoredProcedure("UserList", returnsJson: false, returnsJsonArray: false);

        var source = await gen.GetStoredProcedureExtensionsCodeAsync(schema, new List<Definition.StoredProcedure> { sp });
        var code = source.ToString();

        code.Should().Contain("UserListAsync");
        code.Should().NotContain("UserListDeserializeAsync");
    }

    private class TestConsoleService : SpocR.Services.IConsoleService
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
        public SpocR.Services.Choice GetSelection(string prompt, System.Collections.Generic.List<string> options) => new(-1, string.Empty);
        public SpocR.Services.Choice GetSelectionMultiline(string prompt, System.Collections.Generic.List<string> options) => new(-1, string.Empty);
        public bool GetYesNo(string prompt, bool isDefaultConfirmed, System.ConsoleColor? promptColor = null, System.ConsoleColor? promptBgColor = null) => true;
        public string GetString(string prompt, string defaultValue = "", System.ConsoleColor? promptColor = null) => defaultValue;
        public void PrintTitle(string title) { }
        public void PrintImportantTitle(string title) { }
        public void PrintSubTitle(string title) { }
        public void PrintSummary(System.Collections.Generic.IEnumerable<string> summary, string headline = "") { }
        public void PrintTotal(string total) { }
        public void PrintDryRunMessage(string message = "") { }
        public void PrintConfiguration(ConfigurationModel config) { }
        public void PrintFileActionMessage(string fileName, Enums.FileActionEnum action) { }
        public void PrintCorruptConfigMessage(string message) { }
        public void StartProgress(string message) { }
        public void CompleteProgress(bool success = true, string message = "") { }
        public void UpdateProgressStatus(string status, bool success = true, int? percentage = null) { }
    }
}

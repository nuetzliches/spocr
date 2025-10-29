using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using Shouldly;
using SpocR.CodeGenerators.Models;
using SpocR.CodeGenerators.Utils;
using SpocR.Services;
using SpocR.Infrastructure;
using SpocR.Models;
using SpocR.Contracts;
using SpocR.SpocRVNext.Data.Models;
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

    private sealed class FakeMetadataProvider : ISchemaMetadataProvider
    {
        public IReadOnlyList<SchemaModel> Schemas { get; set; } = new List<SchemaModel>();
        public IReadOnlyList<SchemaModel> GetSchemas() => Schemas;
    }

    private static (StoredProcedureGenerator gen, FileManager<ConfigurationModel> fileManager, FakeMetadataProvider meta) CreateGenerator(ConfigurationModel config)
    {
        var spocr = new SpocrService();
        var fileManager = new FileManager<ConfigurationModel>(spocr, "spocr.json", config);
        // force config to be used as-is
        fileManager.OverwriteWithConfig = config;

        var output = new OutputService(fileManager, new TestConsoleService());
        const string storedProcTemplate = "using System;\nusing System.Collections.Generic;\nusing Microsoft.Data.SqlClient;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Source.DataContext.Models;\nusing Source.DataContext.Outputs;\nnamespace Source.DataContext.StoredProcedures.Schema { public static class StoredProcedureExtensions { public static Task<CrudResult> CrudActionAsync(this IAppDbContextPipe context, Input input, CancellationToken cancellationToken){ if(context==null){ throw new ArgumentNullException(\"context\"); } var parameters = new List<SqlParameter>(); return context.ExecuteSingleAsync<CrudResult>(\"schema.CrudAction\", parameters, cancellationToken); } public static Task<CrudResult> CrudActionAsync(this IAppDbContext context, Input input, CancellationToken cancellationToken){ return context.CreatePipe().CrudActionAsync(input, cancellationToken); } } }";
        var templateManager = new TemplateManager(output, fileManager);
        InjectStoredProcedureTemplate(templateManager, storedProcTemplate);
        var meta = new FakeMetadataProvider();
        var generator = new StoredProcedureGenerator(fileManager, output, new TestConsoleService(), templateManager, meta);
        return (generator, fileManager, meta);
    }

    private static (Definition.Schema schema, Definition.StoredProcedure sp, SchemaModel schemaModel, StoredProcedureModel spModel) CreateStoredProcedure(string name, bool returnsJson, bool returnsJsonArray)
    {
        var resultColumns = returnsJson
            ? new[] { new StoredProcedureContentModel.ResultColumn { Name = "Id" } }
            : new[] { new StoredProcedureContentModel.ResultColumn { Name = "UserName", SqlTypeName = "nvarchar", IsNullable = false } };
        ;

        var content = new StoredProcedureContentModel
        {
            ResultSets = new[]
            {
                new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = returnsJson,
                    ReturnsJsonArray = returnsJson && returnsJsonArray,
                    Columns = resultColumns
                }
            }
        };

        // manually set private backing via constructor then attach content
        var spModel = new StoredProcedureModel(new StoredProcedure { Name = name, SchemaName = "dbo", Modified = DateTime.UtcNow })
        {
            Input = new List<StoredProcedureInputModel>(),
        };
        spModel.Content = content;

        var schemaModel = new SchemaModel
        {
            Name = "dbo",
            StoredProcedures = new List<StoredProcedureModel> { spModel }
        };

        var defSchema = Definition.ForSchema(schemaModel);
        var defSp = Definition.ForStoredProcedure(spModel, defSchema);
        return (defSchema, defSp, schemaModel, spModel);
    }

    [Fact]
    public async Task Generates_Raw_Only_For_Json_Array()
    {
        var config = new SpocrService().GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";

        var (gen, _, meta) = CreateGenerator(config);
        var (schema, sp, schemaModel, spModel) = CreateStoredProcedure("UserListAsJson", returnsJson: true, returnsJsonArray: true);
        meta.Schemas = new[] { schemaModel };

        var source = await gen.GetStoredProcedureExtensionsCodeAsync(schema, new List<Definition.StoredProcedure> { sp });
        var code = source.ToString();

        code.ShouldContain("Task<string> UserListAsJsonAsync");
        code.ShouldNotContain("UserListAsJsonDeserializeAsync");
        code.ShouldNotContain("ReadJsonDeserializeAsync<List<UserListAsJson>>");
    }

    [Fact]
    public async Task Generates_Raw_Only_For_Json_Single()
    {
        var config = new SpocrService().GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";

        var (gen, _, meta) = CreateGenerator(config);
        var (schema, sp, schemaModel, spModel) = CreateStoredProcedure("UserFindAsJson", returnsJson: true, returnsJsonArray: false);
        meta.Schemas = new[] { schemaModel };

        var source = await gen.GetStoredProcedureExtensionsCodeAsync(schema, new List<Definition.StoredProcedure> { sp });
        var code = source.ToString();

        code.ShouldContain("Task<string> UserFindAsJsonAsync");
        code.ShouldNotContain("UserFindAsJsonDeserializeAsync");
        code.ShouldNotContain("ReadJsonDeserializeAsync<UserFindAsJson>");
    }

    [Fact]
    public async Task Generates_Only_Raw_For_NonJson()
    {
        var config = new SpocrService().GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";

        var (gen, _, meta) = CreateGenerator(config);
        var (schema, sp, schemaModel, spModel) = CreateStoredProcedure("UserList", returnsJson: false, returnsJsonArray: false);
        meta.Schemas = new[] { schemaModel };

        var source = await gen.GetStoredProcedureExtensionsCodeAsync(schema, new List<Definition.StoredProcedure> { sp });
        var code = source.ToString();

        code.ShouldContain("UserListAsync");
        code.ShouldNotContain("UserListDeserializeAsync");
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

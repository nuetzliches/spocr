using System.Collections.Generic;
using System.Linq;
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

public class ModelGeneratorJsonEmptyModelTests
{
    private sealed class FakeMetadataProvider : ISchemaMetadataProvider
    {
        public IReadOnlyList<SchemaModel> Schemas { get; set; } = new List<SchemaModel>();
        public IReadOnlyList<SchemaModel> GetSchemas() => Schemas;
    }

    private static (ModelGenerator gen, Definition.Schema schema, Definition.StoredProcedure sp, FakeMetadataProvider meta) Arrange(string spName)
    {
        var spocr = new SpocrService();
        var config = spocr.GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";

        var fileManager = new FileManager<ConfigurationModel>(spocr, "spocr.json", config);
        fileManager.OverwriteWithConfig = config;
        var output = new OutputService(fileManager, new TestConsoleService());
        var templateManager = new TemplateManager(output, fileManager);

        // Minimal template for model (simulate Models/Model.cs)
        const string modelTemplate = "namespace Source.DataContext.Models.Schema { public class Model { public string __TemplateProperty__ { get; set; } } }";
        var tree = CSharpSyntaxTree.ParseText(modelTemplate);
        var root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)tree.GetRoot();
        var templateField = typeof(TemplateManager).GetField("_templateCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cacheObj = templateField?.GetValue(templateManager);
        if (cacheObj is System.Collections.IDictionary dict)
        {
            dict["Models/Model.cs"] = root;
        }

        // Create StoredProcedureModel with ReturnsJson but without JSON columns and Outputs
        var spModel = new StoredProcedureModel(new SpocR.DataContext.Models.StoredProcedure { Name = spName, SchemaName = "dbo" })
        {
            Input = new List<StoredProcedureInputModel>(),
        };
        spModel.Content = new StoredProcedureContentModel
        {
            ResultSets = new[]
            {
                new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = true,
                    ReturnsJsonArray = true,
                    ReturnsJsonWithoutArrayWrapper = false,
                    Columns = System.Array.Empty<StoredProcedureContentModel.ResultColumn>()
                }
            }
        };


        var schemaModel = new SchemaModel
        {
            Name = "dbo",
            StoredProcedures = new List<StoredProcedureModel> { spModel }
        };
        var defSchema = Definition.ForSchema(schemaModel);
        var defSp = Definition.ForStoredProcedure(spModel, defSchema);

        var meta = new FakeMetadataProvider { Schemas = new[] { schemaModel } };
        var gen = new ModelGenerator(fileManager, output, new TestConsoleService(), templateManager, meta);
        return (gen, defSchema, defSp, meta);
    }

    [Fact]
    public async Task Generates_Xml_Doc_For_Empty_Json_Model()
    {
        var (gen, schema, sp, _) = Arrange("UserListAsJson");
        var text = await gen.GetModelTextForStoredProcedureAsync(schema, sp);
        var code = text.ToString();

        // The generator should inject documentation comment; tolerate absence only if RawJson present (future-proofing)
        (code.Contains("Generated JSON model (no columns detected at generation time)") || code.Contains("RawJson"))
            .Should().BeTrue("either the explanatory doc comment or the RawJson property must exist");
        code.Should().Contain("class UserListAsJson");
        // Ensure no properties other than template removal result
        code.Should().NotContain("__TemplateProperty__");
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
}

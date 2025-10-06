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

/// <summary>
/// Tests for multi-result set model generation naming convention:
/// First result set keeps original stored procedure base name, subsequent sets get suffix _1, _2, ...
/// </summary>
public class ModelGeneratorMultiResultSetTests
{
    private sealed class FakeMetadataProvider : ISchemaMetadataProvider
    {
        public IReadOnlyList<SchemaModel> Schemas { get; set; } = new List<SchemaModel>();
        public IReadOnlyList<SchemaModel> GetSchemas() => Schemas;
    }

    private static (ModelGenerator gen, Definition.Schema schema, Definition.StoredProcedure sp, FakeMetadataProvider meta) Arrange()
    {
        var spocr = new SpocrService();
        var config = spocr.GetDefaultConfiguration(appNamespace: "Test.App");
        config.Project.Output.Namespace = "Test.App";

        var fileManager = new FileManager<ConfigurationModel>(spocr, "spocr.json", config) { OverwriteWithConfig = config };
        var output = new OutputService(fileManager, new TestConsoleService());
        var templateManager = new TemplateManager(output, fileManager);

        // Inject minimal model template
        const string modelTemplate = "namespace Source.DataContext.Models.Schema { public class Model { public string __TemplateProperty__ { get; set; } } }";
        var tree = CSharpSyntaxTree.ParseText(modelTemplate);
        var root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)tree.GetRoot();
        var templateField = typeof(TemplateManager).GetField("_templateCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cacheObj = templateField?.GetValue(templateManager);
        if (cacheObj is System.Collections.IDictionary dict)
        {
            dict["Models/Model.cs"] = root;
        }

        // Build StoredProcedure with 3 result sets
        var spModel = new StoredProcedureModel(new SpocR.DataContext.Models.StoredProcedure { Name = "UserReport", SchemaName = "dbo" })
        {
            Input = new List<StoredProcedureInputModel>()
        };
        spModel.Content = new StoredProcedureContentModel
        {
            ResultSets = new[]
            {
                new StoredProcedureContentModel.ResultSet // base model (UserReport)
                {
                    ReturnsJson = false,
                    Columns = new[]
                    {
                        new StoredProcedureContentModel.ResultColumn { Name = "UserName", SqlTypeName = "nvarchar", IsNullable = false }
                    }
                },
                new StoredProcedureContentModel.ResultSet // second model (UserReport_1)
                {
                    ReturnsJson = false,
                    Columns = new[]
                    {
                        new StoredProcedureContentModel.ResultColumn { Name = "OrderCount", SqlTypeName = "int", IsNullable = false }
                    }
                },
                new StoredProcedureContentModel.ResultSet // third model (UserReport_2)
                {
                    ReturnsJson = false,
                    Columns = new[]
                    {
                        new StoredProcedureContentModel.ResultColumn { Name = "LastLogin", SqlTypeName = "datetime", IsNullable = true }
                    }
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
    public async Task Generates_Multiple_Result_Set_Models_With_Suffixes()
    {
        var (gen, schema, sp, _) = Arrange();

        // New behavior: caller must manually split multi-result sets (generator expects exactly one set per call).
        // Validate that calling with original multi-set SP throws, enforcing explicit splitting.
        var act = async () => await gen.GetModelTextForStoredProcedureAsync(schema, sp);
        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*expects exactly one ResultSet*");

        // Now simulate explicit per-set splitting (what production code does) and validate outputs
        for (int r = 0; r < sp.ResultSets.Count; r++)
        {
            var rs = sp.ResultSets[r];
            var splitName = r == 0 ? sp.Name : sp.Name + "_" + r;
            var synthetic = new StoredProcedureModel(new SpocR.DataContext.Models.StoredProcedure
            {
                Name = splitName,
                SchemaName = schema.Name
            })
            {
                Content = new StoredProcedureContentModel
                {
                    ResultSets = new[] { rs }
                }
            };
            var defSynthetic = Definition.ForStoredProcedure(synthetic, schema);
            var text = await gen.GetModelTextForStoredProcedureAsync(schema, defSynthetic);
            var code = text.ToString();
            code.Should().Contain($"class {splitName}");
            if (r == 0) code.Should().Contain("UserName");
            if (r == 1) code.Should().Contain("OrderCount");
            if (r == 2) code.Should().Contain("LastLogin");
            code.Should().NotContain("__TemplateProperty__");
        }
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

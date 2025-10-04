using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpocR.CodeGenerators.Base;
using SpocR.CodeGenerators.Utils;
using SpocR.Contracts;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

public class ModelGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService,
    TemplateManager templateManager,
    ISchemaMetadataProvider metadataProvider
) : GeneratorBase(configFile, output, consoleService)
{
    public async Task<SourceText> GetModelTextForStoredProcedureAsync(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        // Load and process the template with the template manager
        var root = await templateManager.GetProcessedTemplateAsync("Models/Model.cs", schema.Name, storedProcedure.Name);

        // Generate properties
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];
        // Unified model: Prefer ResultSet columns (non-JSON) or JSON result columns. Classic outputs kept as fallback until fully removed.
        var resultColumns = storedProcedure.Columns?.ToList() ?? [];
        var hasResultColumns = resultColumns.Any();

        if (hasResultColumns && !storedProcedure.ReturnsJson)
        {
            foreach (var col in resultColumns)
            {
                if (string.IsNullOrWhiteSpace(col.Name)) continue;
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var propertyIdentifier = SyntaxFactory.ParseToken($" {col.Name.FirstCharToUpper()} ");
                // Use SqlTypeName metadata if available
                var inferredType = !string.IsNullOrWhiteSpace(col.SqlTypeName)
                    ? ParseTypeFromSqlDbTypeName(col.SqlTypeName, col.IsNullable ?? true).ToString()
                    : "string";
                var nonJsonProperty = propertyNode
                    .WithType(SyntaxFactory.ParseTypeName(inferredType))
                    .WithIdentifier(propertyIdentifier);
                root = root.AddProperty(ref classNode, nonJsonProperty);
            }
        }
        // JSON result -> generate properties from JSON Columns (string MVP)
        else if (storedProcedure.ReturnsJson && (storedProcedure.Columns?.Any() ?? false))
        {
            foreach (var col in storedProcedure.Columns)
            {
                if (string.IsNullOrWhiteSpace(col.Name))
                {
                    continue;
                }

                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var propertyIdentifier = SyntaxFactory.ParseToken($" {col.Name.FirstCharToUpper()} ");

                var inferredType = "string"; // default fallback
                if (!string.IsNullOrWhiteSpace(col.SqlTypeName))
                {
                    inferredType = ParseTypeFromSqlDbTypeName(col.SqlTypeName, col.IsNullable ?? true).ToString();
                }

                var jsonProperty = propertyNode
                    .WithType(SyntaxFactory.ParseTypeName(inferredType))
                    .WithIdentifier(propertyIdentifier);

                root = root.AddProperty(ref classNode, jsonProperty);
            }
        }
        // Remove template placeholder property first
        root = TemplateManager.RemoveTemplateProperty(root);

        // 3) JSON result but no columns extracted -> empty model + warning (keeps method signature valid)
        if (!hasResultColumns && !(storedProcedure.Columns?.Any() ?? false) && storedProcedure.ReturnsJson)
        {
            consoleService.Warn($"No JSON columns extracted for stored procedure '{storedProcedure.Name}'. Generated empty model.");
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            if (!classNode.Members.OfType<PropertyDeclarationSyntax>().Any())
            {
                var xml =
                    "/// <summary>Generated JSON model (no columns detected at generation time). The underlying stored procedure returns JSON, but its column structure couldn't be statically inferred (e.g. wildcard, dynamic SQL, variable JSON payload).</summary>" + System.Environment.NewLine +
                    "/// <remarks>Consider rewriting the procedure with an explicit SELECT list or stable aliases so properties can be generated.</remarks>" + System.Environment.NewLine;
                classNode = classNode.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(xml).AddRange(classNode.GetLeadingTrivia()));
                root = root.ReplaceNode(nsNode, nsNode.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(classNode)));
            }
        }

        return TemplateManager.GenerateSourceText(root);
    }

    public async Task GenerateDataContextModels(bool isDryRun)
    {
        var schemas = metadataProvider.GetSchemas()
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures
                .Where(i => i.ReadWriteKind == Definition.ReadWriteKindEnum.Read).ToList();

            if (storedProcedures.Count == 0)
            {
                continue;
            }

            var dataContextModelPath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Models.Path);
            var path = Path.Combine(dataContextModelPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var storedProcedure in storedProcedures)
            {
                // Multi-ResultSet strategy:
                // - First result set keeps existing base model name (storedProcedure.Name)
                // - Additional result sets (index >=1) get suffix _1, _2, ...
                //   Suffix number = resultSetIndex (0-based) to keep it predictable.
                var resultSets = storedProcedure.ResultSets;
                // Fallback: treat existing Columns/Flags as single primary (backward compatibility through Definition wrapper)
                if (resultSets == null || resultSets.Count == 0)
                {
                    await WriteSingleModelAsync(schema, storedProcedure, path, isDryRun);
                    continue;
                }

                for (var rIndex = 0; rIndex < resultSets.Count; rIndex++)
                {
                    var modelSp = storedProcedure;
                    // We need a lightweight clone to override column exposure via Definition wrapper semantics.
                    // Instead of altering Definition we temporarily project a synthetic StoredProcedureModel when index>0.
                    // NOTE: Minimal invasive: reuse GetModelTextForStoredProcedureAsync which relies on Definition.StoredProcedure.Columns/ReturnsJson.
                    if (rIndex > 0)
                    {
                        // Build synthetic model object replicating name with suffix and mapping only the target result set as primary.
                        var suffixName = storedProcedure.Name + "_" + rIndex; // _1, _2, ...
                        var spModel = new SpocR.Models.StoredProcedureModel(new SpocR.DataContext.Models.StoredProcedure
                        {
                            Name = suffixName,
                            SchemaName = schema.Name
                        })
                        {
                            Content = new StoredProcedureContentModel
                            {
                                ResultSets = new[] { resultSets[rIndex] }
                            }
                        };
                        // Wrap again into Definition to leverage existing logic
                        modelSp = Definition.ForStoredProcedure(spModel, schema);
                    }

                    await WriteSingleModelAsync(schema, modelSp, path, isDryRun);
                }
            }
        }
    }

    private async Task WriteSingleModelAsync(Definition.Schema schema, Definition.StoredProcedure storedProcedure, string path, bool isDryRun)
    {
        var hasResultCols = storedProcedure.Columns?.Any() ?? false;
        var isScalarResultCols = hasResultCols && !storedProcedure.ReturnsJson && storedProcedure.Columns.Count == 1;
        if (!storedProcedure.ReturnsJson && isScalarResultCols)
            return; // skip scalar tabular model

        var fileName = $"{storedProcedure.Name}.cs";
        var fileNameWithPath = Path.Combine(path, fileName);
        var sourceText = await GetModelTextForStoredProcedureAsync(schema, storedProcedure);
        await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);
    }
}

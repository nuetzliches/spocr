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
    TemplateManager templateManager
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
        // 1) Classic (declarative) outputs
        var outputs = storedProcedure.Output?.ToList() ?? [];
        var hasClassicOutputs = outputs.Any();
        if (hasClassicOutputs)
        {
            foreach (var item in outputs)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];

                var propertyIdentifier = SyntaxFactory.ParseToken($" {item.Name.FirstCharToUpper()} ");
                propertyNode = propertyNode
                    .WithType(ParseTypeFromSqlDbTypeName(item.SqlTypeName, item.IsNullable ?? false))
                    .WithIdentifier(propertyIdentifier);

                root = root.AddProperty(ref classNode, propertyNode);
            }
        }
        // 2) JSON result without classic outputs -> generate properties from JsonColumns (string MVP)
        else if (storedProcedure.ReturnsJson && (storedProcedure.JsonColumns?.Any() ?? false))
        {
            foreach (var col in storedProcedure.JsonColumns)
            {
                if (string.IsNullOrWhiteSpace(col.Name))
                {
                    continue;
                }

                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var propertyIdentifier = SyntaxFactory.ParseToken($" {col.Name.FirstCharToUpper()} ");

                // Type inference: currently SchemaManager already mapped Outputs for JSON to StoredProcedure.Output when present.
                // Here we attempt a naive inference only when classic outputs were NOT produced.
                var inferredType = "string"; // default fallback
                // Type inference attempt based on enriched outputs (always on)
                if (storedProcedure.Output?.Any() == true)
                {
                    // If SchemaManager enriched outputs, try to match by name
                    var match = storedProcedure.Output.FirstOrDefault(o => o.Name.Equals(col.Name, System.StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        inferredType = ParseTypeFromSqlDbTypeName(match.SqlTypeName, match.IsNullable ?? true).ToString();
                    }
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
        if (!hasClassicOutputs && !(storedProcedure.JsonColumns?.Any() ?? false) && storedProcedure.ReturnsJson)
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
        var schemas = ConfigFile.Config.Schema
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures
                .Where(i => i.ReadWriteKind == Definition.ReadWriteKindEnum.Read).ToList();

            if (!(storedProcedures.Count != 0))
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
                var hasClassicOutputs = storedProcedure.Output?.Any() ?? false;
                var isScalarClassic = hasClassicOutputs && storedProcedure.Output!.Count() == 1;

                // Classic scalar output without JSON -> no model required
                if (isScalarClassic && !storedProcedure.ReturnsJson)
                {
                    continue;
                }

                var fileName = $"{storedProcedure.Name}.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = await GetModelTextForStoredProcedureAsync(schema, storedProcedure);

                await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

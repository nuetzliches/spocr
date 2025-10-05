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
        // Load template
        var root = await templateManager.GetProcessedTemplateAsync("Models/Model.cs", schema.Name, storedProcedure.Name);
        var nsBase = root.Members[0] as BaseNamespaceDeclarationSyntax;
        if (nsBase == null) throw new System.InvalidOperationException("Template must contain a namespace.");
        var classNode = nsBase.Members.OfType<ClassDeclarationSyntax>().First();
        var templateProperty = classNode.Members.OfType<PropertyDeclarationSyntax>().First();

        var resultColumns = storedProcedure.Columns?.ToList() ?? [];
        var hasResultColumns = resultColumns.Any();

        // Local helpers
        string InferType(string sqlType, bool? nullable)
        {
            if (string.IsNullOrWhiteSpace(sqlType)) return "string";
            return ParseTypeFromSqlDbTypeName(sqlType, nullable ?? true).ToString();
        }

        ClassDeclarationSyntax AddProperty(ClassDeclarationSyntax cls, string name, string typeName)
        {
            var prop = templateProperty
                .WithType(SyntaxFactory.ParseTypeName(typeName))
                .WithIdentifier(SyntaxFactory.ParseToken($" {name.FirstCharToUpper()} "));
            return cls.AddMembers(prop);
        }

        ClassDeclarationSyntax BuildNestedClass(string className, StoredProcedureContentModel.JsonResultModel jsonModel)
        {
            var nested = SyntaxFactory.ClassDeclaration(className)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
            if (jsonModel?.Columns != null)
            {
                foreach (var c in jsonModel.Columns)
                {
                    if (string.IsNullOrWhiteSpace(c.Name)) continue;
                    var nType = InferType(c.SqlTypeName, c.IsNullable);
                    var nProp = SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName(nType), SyntaxFactory.Identifier(c.Name.FirstCharToUpper()))
                        .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                        .AddAccessorListAccessors(
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                        );
                    nested = nested.AddMembers(nProp);
                }
            }
            return nested;
        }

        if (hasResultColumns && !storedProcedure.ReturnsJson)
        {
            foreach (var col in resultColumns)
            {
                if (string.IsNullOrWhiteSpace(col.Name)) continue;
                classNode = AddProperty(classNode, col.Name, InferType(col.SqlTypeName, col.IsNullable));
            }
        }
        else if (storedProcedure.ReturnsJson && (storedProcedure.Columns?.Any() ?? false))
        {
            foreach (var col in storedProcedure.Columns)
            {
                if (string.IsNullOrWhiteSpace(col.Name)) continue;
                if (col.JsonResult != null && (col.JsonResult.Columns?.Any() ?? false))
                {
                    var nestedClassName = col.Name.FirstCharToUpper().Trim();
                    if (nestedClassName.EndsWith("s") && col.JsonResult.ReturnsJsonArray)
                        nestedClassName = nestedClassName.TrimEnd('s');

                    if (!classNode.Members.OfType<ClassDeclarationSyntax>().Any(c => c.Identifier.Text == nestedClassName))
                    {
                        var nested = BuildNestedClass(nestedClassName, col.JsonResult);
                        classNode = classNode.AddMembers(nested);
                    }
                    var propertyType = col.JsonResult.ReturnsJsonArray
                        ? $"System.Collections.Generic.List<{nestedClassName}>"
                        : nestedClassName;
                    classNode = AddProperty(classNode, col.Name, propertyType);
                }
                else
                {
                    classNode = AddProperty(classNode, col.Name, InferType(col.SqlTypeName, col.IsNullable));
                }
            }

            // Ensure fallback properties added (defensive)
            var existing = classNode.Members.OfType<PropertyDeclarationSyntax>().Select(p => p.Identifier.Text).ToHashSet();
            foreach (var col in storedProcedure.Columns.Where(c => c.JsonResult != null && (c.JsonResult.Columns?.Any() ?? false)))
            {
                var propName = col.Name.FirstCharToUpper();
                if (existing.Contains(propName)) continue;
                var elementName = propName;
                if (elementName.EndsWith("s") && col.JsonResult.ReturnsJsonArray)
                    elementName = elementName.TrimEnd('s');
                if (!classNode.Members.OfType<ClassDeclarationSyntax>().Any(c => c.Identifier.Text == elementName))
                {
                    classNode = classNode.AddMembers(SyntaxFactory.ClassDeclaration(elementName).AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword)));
                }
                var typeName = col.JsonResult.ReturnsJsonArray ? $"System.Collections.Generic.List<{elementName}>" : elementName;
                classNode = AddProperty(classNode, propName, typeName);
            }
        }

        // Remove template placeholder property
        root = TemplateManager.RemoveTemplateProperty(root.ReplaceNode(nsBase, nsBase.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(classNode))));

        if (!hasResultColumns && !(storedProcedure.Columns?.Any() ?? false) && storedProcedure.ReturnsJson)
        {
            consoleService.Warn($"No JSON columns extracted for stored procedure '{storedProcedure.Name}'. Generated empty model.");
            // Add doc comment if still empty
            classNode = root.Members.OfType<BaseNamespaceDeclarationSyntax>().First().Members.OfType<ClassDeclarationSyntax>().First();
            if (!classNode.Members.OfType<PropertyDeclarationSyntax>().Any())
            {
                var xml = "/// <summary>Generated JSON model (no columns detected at generation time). The underlying stored procedure returns JSON, but its column structure couldn't be statically inferred.</summary>" + System.Environment.NewLine +
                          "/// <remarks>Consider rewriting the procedure with an explicit SELECT list or stable aliases so properties can be generated.</remarks>" + System.Environment.NewLine;
                var updated = classNode.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(xml).AddRange(classNode.GetLeadingTrivia()));
                var currentNs = (BaseNamespaceDeclarationSyntax)root.Members[0];
                root = root.ReplaceNode(classNode, updated);
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

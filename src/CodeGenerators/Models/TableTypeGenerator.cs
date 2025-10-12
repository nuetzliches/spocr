using System;
using System.Data;
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
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

public class TableTypeGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService,
    TemplateManager templateManager,
    ISchemaMetadataProvider metadataProvider
) : GeneratorBase(configFile, output, consoleService)
{
    public async Task<SourceText> GetTableTypeTextAsync(Definition.Schema schema, Definition.TableType tableType)
    {
        // Load and process the template with the template manager
        var root = await templateManager.GetProcessedTemplateAsync("TableTypes/TableType.cs", schema.Name, GetTypeNameForTableType(tableType));

        // If its an extension, add usings for the lib
        if (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Extension)
        {
            var libModelUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}.TableTypes"));
            root = root.AddUsings(libModelUsingDirective).NormalizeWhitespace();
        }

        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Create Properties
        if (tableType.Columns != null)
        {
            foreach (var column in tableType.Columns)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];

                var propertyIdentifier = SyntaxFactory.ParseToken($" {column.Name} ");

                propertyNode = propertyNode
                    .WithType(ParseTypeFromSqlDbTypeName(column.SqlTypeName, column.IsNullable ?? false));

                propertyNode = propertyNode
                    .WithIdentifier(propertyIdentifier);

                // Add Attribute for NVARCHAR with MaxLength
                if (column.SqlTypeName.Equals(SqlDbType.NVarChar.ToString(), StringComparison.InvariantCultureIgnoreCase)
                    && column.MaxLength.HasValue)
                {
                    var attributes = propertyNode.AttributeLists.Add(
                        SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                            SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("MaxLength"), SyntaxFactory.ParseAttributeArgumentList($"({column.MaxLength})"))
                        )).NormalizeWhitespace());

                    propertyNode = propertyNode.WithAttributeLists(attributes);
                }

                root = root.AddProperty(ref classNode, propertyNode);
            }
        }

        // Remove template placeholder property
        root = TemplateManager.RemoveTemplateProperty(root);

        return TemplateManager.GenerateSourceText(root);
    }

    public async Task GenerateDataContextTableTypesAsync(bool isDryRun)
    {
        // Ensure ITableType interface nur für Nicht-Extension Rollen.
        // Extensions sollen die Definition aus dem LibNamespace referenzieren und keine lokale Kopie erzeugen.
        var skipITableType = false;
#pragma warning disable CS0618
        if (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Extension)
        {
            skipITableType = true;
            ConsoleService.Verbose("[tabletypes] Skipping ITableType.cs generation for Extension role (uses Lib namespace).");
        }
#pragma warning restore CS0618
        if (!skipITableType)
        {
            try
            {
                var tableTypesRoot = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.TableTypes.Path);
                if (!Directory.Exists(tableTypesRoot) && !isDryRun)
                {
                    Directory.CreateDirectory(tableTypesRoot);
                }
                var interfaceTemplatePath = Path.Combine(Output.GetOutputRootDir().FullName, "DataContext", "TableTypes", "ITableType.base.cs");
                var interfaceTargetPath = Path.Combine(tableTypesRoot, "ITableType.cs");
                if (File.Exists(interfaceTemplatePath))
                {
                    if (!File.Exists(interfaceTargetPath))
                    {
                        var raw = await File.ReadAllTextAsync(interfaceTemplatePath);
                        var configuredRootNs = ConfigFile.Config.Project.Output.Namespace?.Trim();
                        if (string.IsNullOrWhiteSpace(configuredRootNs)) throw new InvalidOperationException("Missing Project.Output.Namespace");
                        var targetNs = ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib
                            ? $"{configuredRootNs}.TableTypes"
                            : $"{configuredRootNs}.DataContext.TableTypes";
                        raw = raw.Replace("namespace Source.DataContext.TableTypes", $"namespace {targetNs}");
                        await Output.WriteAsync(interfaceTargetPath, SourceText.From(raw), isDryRun);
                    }
                }
            }
            catch (Exception itx)
            {
                ConsoleService.Verbose($"[tabletypes] Skipped ITableType generation: {itx.Message}");
            }
        }

        var schemas = metadataProvider.GetSchemas()
            .Where(i => i.TableTypes?.Any() ?? false)
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var tableTypes = schema.TableTypes;

            var dataContextTableTypesPath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.TableTypes.Path);
            var path = Path.Combine(dataContextTableTypesPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var tableType in tableTypes)
            {
                var fileName = $"{tableType.Name}TableType.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = await GetTableTypeTextAsync(schema, tableType);

                await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

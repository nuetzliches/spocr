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
    TemplateManager templateManager
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
        var schemas = ConfigFile.Config.Schema
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

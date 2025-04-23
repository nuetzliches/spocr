using System;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpocR.CodeGenerators.Base;
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
    IReportService reportService
) : GeneratorBase(configFile, output, reportService)
{
    public SourceText GetTableTypeText(Definition.Schema schema, Definition.TableType tableType)
    {
        var rootDir = Output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "TableTypes", "TableType.cs"));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // Replace Namespace
        if (ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", ConfigFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", ConfigFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }

        // If its an extension, add usings for the lib
        if (ConfigFile.Config.Project.Role.Kind == ERoleKind.Extension)
        {
            var libModelUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}.TableTypes"));
            root = root.AddUsings(libModelUsingDirective).NormalizeWhitespace();
        }

        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];

        // Replace ClassName
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        var classIdentifier = SyntaxFactory.ParseToken($"{classNode.Identifier.ValueText.Replace("TableType", $"{GetTypeNameForTableType(tableType)}")} ");
        classNode = classNode.WithIdentifier(classIdentifier);

        root = root.ReplaceNode(nsNode, nsNode.AddMembers(classNode));

        // Create Properties
        if (tableType.Columns != null)
        {
            foreach (var column in tableType.Columns)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[1];
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

        // Remove template Property
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[1];
        root = root.ReplaceNode(classNode, classNode.WithMembers([.. classNode.Members.Cast<PropertyDeclarationSyntax>().Skip(1)]));

        // Remove template Class
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        root = root.ReplaceNode(nsNode, nsNode.WithMembers([.. nsNode.Members.Cast<ClassDeclarationSyntax>().Skip(1)]));

        return root.NormalizeWhitespace().GetText();
    }

    public void GenerateDataContextTableTypes(bool isDryRun)
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
                var sourceText = GetTableTypeText(schema, tableType);

                Output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

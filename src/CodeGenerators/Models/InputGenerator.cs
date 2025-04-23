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
using SpocR.Roslyn.Helpers;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

public class InputGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IReportService reportService
) : GeneratorBase(configFile, output, reportService)
{
    public SourceText GetInputTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        var rootDir = Output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "Inputs", "Input.cs"));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // If Inputs contains a TableType, add using for TableTypes
        var schemesForTableTypes = storedProcedure.Input.Where(i => i.IsTableType ?? false)
                             .GroupBy(t => (t.TableTypeSchemaName, t.TableTypeName), (key, group) => new
                             {
                                 TableTypeSchemaName = key.TableTypeSchemaName,
                                 Result = group.First()
                             }).Select(g => g.Result).ToList();

        var tableTypeSchemas = storedProcedure.Input.Where(i => i.IsTableType ?? false)
                                    .GroupBy(t => t.TableTypeSchemaName, (key, group) => key).ToList();

        foreach (var tableTypeSchema in tableTypeSchemas)
        {
            var tableTypeSchemaConfig = ConfigFile.Config.Schema.Find(s => s.Name.Equals(tableTypeSchema));
            // is schema of table type ignored and its an extension?
            var useFromLib = tableTypeSchemaConfig?.Status != SchemaStatusEnum.Build
                && ConfigFile.Config.Project.Role.Kind == ERoleKind.Extension;

            var paramUsingDirective = useFromLib
                                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                                : ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib
                                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
            root = root.AddUsings(paramUsingDirective);
        }

        // Replace Namespace
        if (ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", ConfigFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", ConfigFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }

        // Replace ClassName
        root = root.ReplaceClassName(ci => ci.Replace("Input", $"{storedProcedure.Name}Input"));
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Create obsolete constructor
        var obsoleteContructor = classNode.CreateConstructor($"{storedProcedure.Name}Input");
        root = root.AddObsoleteAttribute(ref obsoleteContructor, "This empty contructor will be removed in vNext. Please use constructor with parameters.");
        root = root.AddConstructor(ref classNode, obsoleteContructor);
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        var inputs = storedProcedure.Input.Where(i => !i.IsOutput).ToList();
        // Constructor with params
        var constructor = classNode.CreateConstructor($"{storedProcedure.Name}Input");
        var parameters = inputs.Select(input =>
        {
            return SyntaxFactory.Parameter(SyntaxFactory.Identifier(GetIdentifierFromSqlInputTableType(input.Name)))
                .WithType(
                    input.IsTableType ?? false
                    ? GetTypeSyntaxForTableType(input)
                    : ParseTypeFromSqlDbTypeName(input.SqlTypeName, input.IsNullable ?? false)
                );
        }).ToArray();

        var constructorParams = constructor.ParameterList.AddParameters(parameters);
        constructor = constructor.WithParameterList(constructorParams);

        foreach (var input in inputs)
        {
            var constructorStatement = ExpressionHelper.AssignmentStatement(TokenHelper.Parse(input.Name).ToString(), GetIdentifierFromSqlInputTableType(input.Name));
            var newStatements = constructor.Body.Statements.Add(constructorStatement);
            constructor = constructor.WithBody(constructor.Body.WithStatements(newStatements));
        }

        root = root.AddConstructor(ref classNode, constructor);
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Generate Properies
        foreach (var item in storedProcedure.Input)
        {
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            var isTableType = item.IsTableType ?? false;
            var propertyType = isTableType
                ? GetTypeSyntaxForTableType(item)
                : ParseTypeFromSqlDbTypeName(item.SqlTypeName, item.IsNullable ?? false);

            var propertyNode = classNode.CreateProperty(propertyType, item.Name);

            if (!isTableType)
            {
                // Add Attribute for NVARCHAR with MaxLength
                if ((item.SqlTypeName?.Equals(System.Data.SqlDbType.NVarChar.ToString(), System.StringComparison.InvariantCultureIgnoreCase) ?? false)
                    && item.MaxLength.HasValue)
                {
                    var attributes = propertyNode.AttributeLists.Add(
                        SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                            SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("MaxLength"), SyntaxFactory.ParseAttributeArgumentList($"({item.MaxLength})"))
                        )).NormalizeWhitespace());

                    propertyNode = propertyNode.WithAttributeLists(attributes);
                }
            }

            root = root.AddProperty(ref classNode, propertyNode);
        }

        return root.NormalizeWhitespace().GetText();
    }

    public void GenerateDataContextInputs(bool isDryRun)
    {
        // Migrate to Version 1.3.2
        if (ConfigFile.Config.Project.Output.DataContext.Inputs == null)
        {
            // Der SpocrService sollte als Abhängigkeit injiziert werden
            var defaultConfig = new SpocrService().GetDefaultConfiguration();
            ConfigFile.Config.Project.Output.DataContext.Inputs = defaultConfig.Project.Output.DataContext.Inputs;
        }

        var schemas = ConfigFile.Config.Schema
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures;

            if (!storedProcedures.Any())
            {
                continue;
            }

            var dataContextInputPath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Inputs.Path);
            var path = Path.Combine(dataContextInputPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var storedProcedure in storedProcedures)
            {
                if (!storedProcedure.HasInputs())
                {
                    continue;
                }
                var fileName = $"{storedProcedure.Name}.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = GetInputTextForStoredProcedure(schema, storedProcedure);

                Output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

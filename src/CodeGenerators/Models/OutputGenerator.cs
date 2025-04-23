using System.Collections.Generic;
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

public class OutputGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IReportService reportService
) : GeneratorBase(configFile, output, reportService)
{
    public SourceText GetOutputTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        var rootDir = Output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "Outputs", "Output.cs"));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // Add Usings
        if (ConfigFile.Config.Project.Role.Kind == ERoleKind.Extension)
        {
            var outputUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}.Outputs"));
            root = root.AddUsings(outputUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
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
        root = root.ReplaceClassName(ci => ci.Replace("Output", storedProcedure.GetOutputTypeName()));

        // Generate Properties
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];
        var outputs = storedProcedure.Input?.Where(i => i.IsOutput).ToList() ?? [];
        foreach (var output in outputs)
        {
            // do not add properties who exists in base class (IOutput)
            // TODO: parse from IOutput
            var ignoredFields = new[] { "ResultId", "RecordId", "RowVersion" };
            if (System.Array.IndexOf(ignoredFields, output.Name.Replace("@", "")) > -1) { continue; }

            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            var propertyIdentifier = TokenHelper.Parse(output.Name);
            propertyNode = propertyNode
                .WithType(ParseTypeFromSqlDbTypeName(output.SqlTypeName, output.IsNullable ?? false));

            propertyNode = propertyNode
                .WithIdentifier(propertyIdentifier);

            root = root.AddProperty(ref classNode, propertyNode);
        }

        // Remove template Property
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        root = root.ReplaceNode(classNode, classNode.WithMembers([.. classNode.Members.Cast<PropertyDeclarationSyntax>().Skip(1)]));

        return root.NormalizeWhitespace().GetText();
    }

    public void GenerateDataContextOutputs(bool isDryRun)
    {
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

            var dataContextOutputsPath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Outputs.Path);
            var path = Path.Combine(dataContextOutputsPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var storedProcedure in storedProcedures)
            {
                if (!storedProcedure.HasOutputs() || storedProcedure.IsDefaultOutput())
                {
                    continue;
                }
                var fileName = $"{storedProcedure.Name}.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = GetOutputTextForStoredProcedure(schema, storedProcedure);

                Output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

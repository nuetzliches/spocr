using System.IO;
using System.Linq;
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
    public SourceText GetModelTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        // Template mit TemplateManager laden und verarbeiten
        var root = templateManager.GetProcessedTemplate("Models/Model.cs", schema.Name, storedProcedure.Name);

        // Properties generieren
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];

        var outputs = storedProcedure.Output?.ToList() ?? [];
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

        // Template-Property entfernen
        root = TemplateManager.RemoveTemplateProperty(root);

        return TemplateManager.GenerateSourceText(root);
    }

    public void GenerateDataContextModels(bool isDryRun)
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
                var isScalar = storedProcedure.Output?.Count() == 1;
                if (isScalar)
                {
                    continue;
                }
                var fileName = $"{storedProcedure.Name}.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = GetModelTextForStoredProcedure(schema, storedProcedure);

                Output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

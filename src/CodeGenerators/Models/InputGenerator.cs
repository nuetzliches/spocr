using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpocR.CodeGenerators.Base;
using SpocR.CodeGenerators.Extensions;
using SpocR.CodeGenerators.Utils;
using SpocR.Contracts;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

public class InputGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService,
    TemplateManager templateManager
) : GeneratorBase(configFile, output, consoleService)
{
    public SourceText GetInputTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        // Template-Verarbeitung mit dem TemplateManager
        var root = templateManager.GetProcessedTemplate("Inputs/Input.cs", schema.Name, $"{storedProcedure.Name}Input");

        // TableType-Imports hinzufügen
        var tableTypeSchemas = storedProcedure.Input
            .Where(i => i.IsTableType ?? false)
            .GroupBy(t => t.TableTypeSchemaName, (key, group) => key)
            .ToList();

        foreach (var tableTypeSchema in tableTypeSchemas)
        {
            var tableTypeSchemaConfig = ConfigFile.Config.Schema.Find(s => s.Name.Equals(tableTypeSchema));
            var usingDirective = templateManager.CreateTableTypeImport(tableTypeSchema, tableTypeSchemaConfig);
            root = root.AddUsings(usingDirective);
        }

        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Obsoleten Constructor hinzufügen
        var obsoleteConstructor = classNode.CreateConstructor($"{storedProcedure.Name}Input");
        obsoleteConstructor = obsoleteConstructor.AddObsoleteAttribute("This empty contructor will be removed in vNext. Please use constructor with parameters.");
        root = root.AddConstructor(ref classNode, obsoleteConstructor);

        // Konstruktor mit Parametern erstellen
        var inputs = storedProcedure.Input.Where(i => !i.IsOutput).ToList();

        // Parameterliste für den Konstruktor erstellen
        var parameters = new List<(string TypeName, string ParamName, string PropertyName)>();
        foreach (var input in inputs)
        {
            var paramName = GetIdentifierFromSqlInputTableType(input.Name);
            var typeName = (input.IsTableType ?? false)
                ? GetTypeSyntaxForTableType(input).ToString()
                : ParseTypeFromSqlDbTypeName(input.SqlTypeName, input.IsNullable ?? false).ToString();
            parameters.Add((typeName, paramName, GetPropertyFromSqlInputTableType(input.Name)));
        }

        // Konstruktor zur Klasse hinzufügen
        root = root.AddParameterizedConstructor($"{storedProcedure.Name}Input", parameters);

        // Properties generieren
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        foreach (var item in storedProcedure.Input)
        {
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            var isTableType = item.IsTableType ?? false;
            var propertyType = isTableType
                ? GetTypeSyntaxForTableType(item)
                : ParseTypeFromSqlDbTypeName(item.SqlTypeName, item.IsNullable ?? false);

            // Attribute hinzufügen für NVarChar mit MaxLength
            if (!isTableType && (item.SqlTypeName?.Equals(System.Data.SqlDbType.NVarChar.ToString(), System.StringComparison.InvariantCultureIgnoreCase) ?? false)
                && item.MaxLength.HasValue)
            {
                var propertyNode = classNode.CreatePropertyWithAttributes(
                    propertyType,
                    item.Name,
                    new Dictionary<string, object> { { "MaxLength", item.MaxLength } });

                root = root.AddProperty(ref classNode, propertyNode);
            }
            else
            {
                var propertyNode = classNode.CreateProperty(propertyType, item.Name);
                root = root.AddProperty(ref classNode, propertyNode);
            }
        }

        return TemplateManager.GenerateSourceText(root);
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

            // Verzeichnis anlegen
            var dataContextInputPath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Inputs.Path);
            var path = Path.Combine(dataContextInputPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            // Dateien generieren
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

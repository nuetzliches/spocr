using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpocR.CodeGenerators.Base;
using SpocR.CodeGenerators.Extensions;
using SpocR.CodeGenerators.Utils;
using SpocR.Contracts;
using SpocR.Extensions;
using SpocR.Infrastructure;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

public class InputGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService,
    TemplateManager templateManager,
    ISchemaMetadataProvider metadataProvider
) : GeneratorBase(configFile, output, consoleService)
{
    public async Task<SourceText> GetInputTextForStoredProcedureAsync(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        // Process template with the template manager
        var root = await templateManager.GetProcessedTemplateAsync("Inputs/Input.cs", schema.Name, $"{storedProcedure.Name}Input");

        // Add table type imports
        var tableTypeSchemas = storedProcedure.Input
            .Where(i => i.IsTableType ?? false)
            .GroupBy(t => t.TableTypeSchemaName, (key, group) => key)
            .ToList();

        var providerSchemas = metadataProvider.GetSchemas();
        foreach (var tableTypeSchema in tableTypeSchemas)
        {
            var tableTypeSchemaConfig = providerSchemas.FirstOrDefault(s => s.Name.Equals(tableTypeSchema, System.StringComparison.OrdinalIgnoreCase));
            var usingDirective = templateManager.CreateTableTypeImport(tableTypeSchema, tableTypeSchemaConfig);
            root = root.AddUsings(usingDirective);
        }

        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Add obsolete constructor
        var obsoleteConstructor = classNode.CreateConstructor($"{storedProcedure.Name}Input");
        obsoleteConstructor = obsoleteConstructor.AddObsoleteAttribute("This empty contructor will be removed in vNext. Please use constructor with parameters.");
        root = root.AddConstructor(ref classNode, obsoleteConstructor);

        // Build constructor with parameters
        var inputs = storedProcedure.Input.Where(i => !i.IsOutput).ToList();

        // Build parameter list for the constructor
        var parameters = new List<(string TypeName, string ParamName, string PropertyName)>();
        foreach (var input in inputs)
        {
            var paramName = GetIdentifierFromSqlInputTableType(input.Name);
            var typeName = (input.IsTableType ?? false)
                ? GetTypeSyntaxForTableType(input).ToString()
                : ParseTypeFromSqlDbTypeName(input.SqlTypeName, input.IsNullable ?? false).ToString();
            parameters.Add((typeName, paramName, GetPropertyFromSqlInputTableType(input.Name)));
        }

        // Add constructor to the class
        root = root.AddParameterizedConstructor($"{storedProcedure.Name}Input", parameters);

        // Generate properties
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

            // Add attribute for NVARCHAR with MaxLength
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

    public async Task GenerateDataContextInputs(bool isDryRun)
    {
        try
        {
            var allSchemas = metadataProvider.GetSchemas();
            var buildSchemas = allSchemas.Where(s => s.Status == SchemaStatusEnum.Build).ToList();
            var spCount = 0;
            foreach (var sc in buildSchemas)
            {
                if (sc.StoredProcedures != null)
                    spCount += sc.StoredProcedures.Count();
            }
            consoleService.Verbose($"[diag-inputs] schemas(build)={buildSchemas.Count} storedProcedures={spCount} dryRun={isDryRun}");
        }
        catch { /* ignore diagnostics */ }
        // Migrate to Version 1.3.2
        if (ConfigFile.Config.Project.Output.DataContext.Inputs == null)
        {
            // SpocrService should be registered as a dependency
            var defaultConfig = new SpocrService().GetDefaultConfiguration();
            ConfigFile.Config.Project.Output.DataContext.Inputs = defaultConfig.Project.Output.DataContext.Inputs;
        }

        var schemas = metadataProvider.GetSchemas()
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures;

            if (!storedProcedures.Any())
            {
                continue;
            }

            // Ensure target directory exists
            var dataContextInputPath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Inputs.Path);
            var path = Path.Combine(dataContextInputPath, schema.Path);
            consoleService.Verbose($"[diag-inputs] targetDir={path}");
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            // Generate files
            foreach (var storedProcedure in storedProcedures)
            {
                if (!storedProcedure.HasInputs())
                {
                    continue;
                }
                consoleService.Verbose($"[diag-inputs] generating input for {schema.Name}.{storedProcedure.Name}");
                var fileName = $"{storedProcedure.Name}.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = await GetInputTextForStoredProcedureAsync(schema, storedProcedure);

                await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

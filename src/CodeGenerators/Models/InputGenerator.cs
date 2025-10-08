using System;
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
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

/// <summary>
/// Generiert Input-Klassen (Parameter DTOs) für Stored Procedures.
/// Unterstützt sowohl block- als auch file-scoped Namespaces in den Vorlagen.
/// </summary>
public class InputGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService,
    TemplateManager templateManager,
    ISchemaMetadataProvider metadataProvider
) : GeneratorBase(configFile, output, consoleService)
{
    private ClassDeclarationSyntax FetchClass(CompilationUnitSyntax root)
    {
        var top = root.Members.First();
        if (top is FileScopedNamespaceDeclarationSyntax fns)
            return fns.Members.OfType<ClassDeclarationSyntax>().First();
        if (top is NamespaceDeclarationSyntax bns)
            return bns.Members.OfType<ClassDeclarationSyntax>().First();
        throw new InvalidOperationException("Unexpected root member in Input template");
    }

    public async Task<SourceText> GetInputTextForStoredProcedureAsync(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        var className = $"{storedProcedure.Name}Input";
        var root = await templateManager.GetProcessedTemplateAsync("Inputs/Input.cs", schema.Name, className);

        // TableType Usings
        var tableTypeSchemas = storedProcedure.Input
            .Where(i => i.IsTableType ?? false)
            .Select(i => i.TableTypeSchemaName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var providerSchemas = metadataProvider.GetSchemas();
        foreach (var tableTypeSchema in tableTypeSchemas)
        {
            var tableTypeSchemaConfig = providerSchemas.FirstOrDefault(s => s.Name.Equals(tableTypeSchema, StringComparison.OrdinalIgnoreCase));
            var usingDirective = templateManager.CreateTableTypeImport(tableTypeSchema, tableTypeSchemaConfig);
            root = root.AddUsings(usingDirective);
        }

        // Namespace ermitteln
        var nsBase = root.Members[0] as BaseNamespaceDeclarationSyntax;
        if (nsBase == null) throw new InvalidOperationException("Template must contain a namespace.");
        var nsName = nsBase.Name.ToString();

        // Positional record: alle Nicht-OUTPUT-Parameter als Primärkonstruktor-Parameter
        var inputs = storedProcedure.Input.Where(i => !i.IsOutput).ToList();
        var paramSegments = new List<string>();
        var needsDataAnnotations = false;
        foreach (var item in inputs)
        {
            var isTableType = item.IsTableType ?? false;
            var typeSyntax = isTableType
                ? GetTypeSyntaxForTableType(item).ToString()
                : ParseTypeFromSqlDbTypeName(item.SqlTypeName, item.IsNullable ?? false).ToString();

            // Property-/Parametername (PascalCase beibehalten)
            var propertyName = GetPropertyFromSqlInputTableType(item.Name);

            string attr = null;
            if (!isTableType
                && (item.SqlTypeName?.Equals(System.Data.SqlDbType.NVarChar.ToString(), StringComparison.InvariantCultureIgnoreCase) ?? false)
                && item.MaxLength.HasValue)
            {
                needsDataAnnotations = true;
                attr = $"[property: MaxLength({item.MaxLength.Value})] ";
            }
            paramSegments.Add($"{attr}{typeSyntax} {propertyName}".TrimStart());
        }

        // Usings zusammentragen (inkl. TableType-Imports)
        var usingLines = root.Usings.Select(u => $"using {u.Name};").ToList();
        if (needsDataAnnotations && !usingLines.Any(l => l.Contains("System.ComponentModel.DataAnnotations", StringComparison.Ordinal)))
        {
            usingLines.Add("using System.ComponentModel.DataAnnotations;");
        }

        var paramBlock = "    " + string.Join(",\n    ", paramSegments);
        var file = string.Join('\n', usingLines) + (usingLines.Count > 0 ? "\n\n" : string.Empty)
                 + $"namespace {nsName};\n\n"
                 + $"public record {className}(\n{paramBlock}\n);\n";

        return SourceText.From(file);
    }

    public async Task GenerateDataContextInputs(bool isDryRun)
    {
        // Sicherstellen, dass Default-Konfig existiert (Legacy Konfigurationen ohne Inputs-Knoten)
        if (ConfigFile.Config.Project.Output.DataContext.Inputs == null)
        {
            var defaultConfig = new SpocrService().GetDefaultConfiguration();
            ConfigFile.Config.Project.Output.DataContext.Inputs = defaultConfig.Project.Output.DataContext.Inputs;
        }

        var schemas = metadataProvider.GetSchemas()
            .Where(s => s.Status == SchemaStatusEnum.Build && (s.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures;
            if (!storedProcedures.Any()) continue;

            var dataContextInputPath = DirectoryUtils.GetWorkingDirectory(
                ConfigFile.Config.Project.Output.DataContext.Path,
                ConfigFile.Config.Project.Output.DataContext.Inputs.Path);

            var schemaPath = Path.Combine(dataContextInputPath, schema.Path);
            if (!Directory.Exists(schemaPath) && !isDryRun)
            {
                Directory.CreateDirectory(schemaPath);
            }

            foreach (var sp in storedProcedures)
            {
                if (!sp.HasInputs()) continue;
                var fileName = $"{sp.Name}.cs";
                var fileNameWithPath = Path.Combine(schemaPath, fileName);
                var sourceText = await GetInputTextForStoredProcedureAsync(schema, sp);
                await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

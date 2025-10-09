using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SpocR.CodeGenerators.Base;
using SpocR.CodeGenerators.Templates;
using SpocR.Contracts;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Modern;

/// <summary>
/// Modern code generator that uses the embedded template engine to generate 
/// .NET 10 compatible code with modern patterns
/// </summary>
public class ModernDbContextGenerator : GeneratorBase
{
    private readonly ITemplateEngine _templateEngine;
    private readonly ISchemaMetadataProvider _metadataProvider;

    public ModernDbContextGenerator(
        FileManager<ConfigurationModel> configFile,
        OutputService output,
        IConsoleService consoleService,
        ITemplateEngine templateEngine,
        ISchemaMetadataProvider metadataProvider)
        : base(configFile, output, consoleService)
    {
        _templateEngine = templateEngine;
        _metadataProvider = metadataProvider;
    }

    /// <summary>
    /// Generates the modern DbContext with all required components
    /// </summary>
    /// <param name="isDryRun">Whether to run in dry-run mode</param>
    /// <param name="generateObsoleteWrappers">Whether to generate obsolete wrappers for backward compatibility</param>
    public async Task GenerateModernDbContextAsync(bool isDryRun, bool generateObsoleteWrappers = true)
    {
        ConsoleService.PrintSubTitle("Generating Modern SpocR DbContext");

        var targetFramework = GetTargetFramework();
        var outputPath = GetModernOutputPath();

        if (!Directory.Exists(outputPath) && !isDryRun)
        {
            Directory.CreateDirectory(outputPath);
        }

        // Generate core DbContext
        await GenerateModernDbContextCoreAsync(outputPath, targetFramework, generateObsoleteWrappers, isDryRun);
        
        // Generate DI extensions
        await GenerateServiceCollectionExtensionsAsync(outputPath, targetFramework, isDryRun);
        
        // Generate Minimal API extensions
        await GenerateMinimalApiExtensionsAsync(outputPath, targetFramework, isDryRun);
        
        // Generate stored procedure specific extensions
        await GenerateStoredProcedureExtensionsAsync(outputPath, targetFramework, isDryRun);

    // Generate input/output/entity models
    await GenerateStoredProcedureModelsAsync(outputPath, targetFramework, isDryRun);

    // Generate table types
    await GenerateTableTypeModelsAsync(outputPath, targetFramework, isDryRun);

        ConsoleService.Success("Modern DbContext generation completed");
    }

    private async Task GenerateModernDbContextCoreAsync(
        string outputPath, 
        TargetFrameworkEnum targetFramework, 
        bool generateObsoleteWrappers, 
        bool isDryRun)
    {
        var placeholders = CreateBasePlaceholders();
        placeholders["ObsoleteOldClasses"] = generateObsoleteWrappers;
        placeholders["InjectApplicationContext"] = true; // TODO: Add to configuration

        var template = await _templateEngine.GetProcessedTemplateAsync(
            TemplateType.ModernAppDbContext, 
            targetFramework, 
            placeholders);

        var filePath = Path.Combine(outputPath, "SpocRDbContext.cs");
        await Output.WriteAsync(filePath, template.GetText(), isDryRun);

        ConsoleService.Verbose($"Generated modern DbContext: {filePath}");
    }

    private async Task GenerateServiceCollectionExtensionsAsync(
        string outputPath, 
        TargetFrameworkEnum targetFramework, 
        bool isDryRun)
    {
        var placeholders = CreateBasePlaceholders();

        var template = await _templateEngine.GetProcessedTemplateAsync(
            TemplateType.ServiceCollectionExtensions, 
            targetFramework, 
            placeholders);

        var filePath = Path.Combine(outputPath, "SpocRServiceCollectionExtensions.cs");
        await Output.WriteAsync(filePath, template.GetText(), isDryRun);

        ConsoleService.Verbose($"Generated DI extensions: {filePath}");
    }

    private async Task GenerateMinimalApiExtensionsAsync(
        string outputPath, 
        TargetFrameworkEnum targetFramework, 
        bool isDryRun)
    {
        var placeholders = CreateBasePlaceholders();
        
        // Add stored procedure information for generated extensions
        var storedProcedures = GetStoredProceduresForGeneration();
        placeholders["StoredProcedures"] = storedProcedures;

        var template = await _templateEngine.GetProcessedTemplateAsync(
            TemplateType.MinimalApiExtensions, 
            targetFramework, 
            placeholders);

        var filePath = Path.Combine(outputPath, "SpocRMinimalApiExtensions.cs");
        await Output.WriteAsync(filePath, template.GetText(), isDryRun);

        ConsoleService.Verbose($"Generated Minimal API extensions: {filePath}");
    }

    private async Task GenerateStoredProcedureExtensionsAsync(
        string outputPath, 
        TargetFrameworkEnum targetFramework, 
        bool isDryRun)
    {
        // Generate schema-specific extensions
        var schemas = _metadataProvider.GetSchemas().Where(s => s.Status == SchemaStatusEnum.Build);

        foreach (var schema in schemas)
        {
            var schemaPath = Path.Combine(outputPath, "Extensions", schema.Name);
            if (!Directory.Exists(schemaPath) && !isDryRun)
            {
                Directory.CreateDirectory(schemaPath);
            }

            var placeholders = CreateBasePlaceholders();
            placeholders["SchemaName"] = schema.Name;
            placeholders["StoredProcedures"] = GetStoredProceduresForSchema(schema.Name);

            var template = await _templateEngine.GetProcessedTemplateAsync(
                TemplateType.StoredProcedureExtensions, 
                targetFramework, 
                placeholders);

            var filePath = Path.Combine(schemaPath, $"{schema.Name}Extensions.cs");
            await Output.WriteAsync(filePath, template.GetText(), isDryRun);

            ConsoleService.Verbose($"Generated schema extensions: {filePath}");
        }
    }

    private async Task GenerateStoredProcedureModelsAsync(
        string outputPath,
        TargetFrameworkEnum targetFramework,
        bool isDryRun)
    {
        var schemas = _metadataProvider.GetSchemas().Where(s => s.Status == SchemaStatusEnum.Build);

        foreach (var schema in schemas)
        {
            var schemaInputsPath = Path.Combine(outputPath, "Inputs", schema.Name);
            var schemaOutputsPath = Path.Combine(outputPath, "Outputs", schema.Name);
            var schemaModelsPath = Path.Combine(outputPath, "Models", schema.Name);

            if (!isDryRun)
            {
                Directory.CreateDirectory(schemaInputsPath);
                Directory.CreateDirectory(schemaOutputsPath);
                Directory.CreateDirectory(schemaModelsPath);
            }

            foreach (var sp in schema.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>())
            {
                // Input model
                if (sp.Input?.Any() == true)
                {
                    var inputPlaceholders = CreateBasePlaceholders();
                    inputPlaceholders["Schema"] = schema.Name;
                    inputPlaceholders["ProcedureName"] = sp.Name;
                    inputPlaceholders["Name"] = sp.Name;
                    inputPlaceholders["Properties"] = sp.Input.Select(p => new Dictionary<string, object>
                    {
                        ["Name"] = p.Name,
                        ["Type"] = MapClrType(p),
                        ["Description"] = p.Name
                    }).ToList();

                    var inputTemplate = await _templateEngine.GetProcessedTemplateAsync(
                        TemplateType.InputModel,
                        targetFramework,
                        inputPlaceholders);

                    var inputFile = Path.Combine(schemaInputsPath, sp.Name + "Input.cs");
                    await Output.WriteAsync(inputFile, inputTemplate.GetText(), isDryRun);
                    ConsoleService.Verbose($"Generated input model: {inputFile}");
                }

                // Output/result model (first result set for now)
                var resultSet = sp.ResultSets?.FirstOrDefault();
                if (resultSet?.Columns?.Any() == true)
                {
                    var outputPlaceholders = CreateBasePlaceholders();
                    outputPlaceholders["Schema"] = schema.Name;
                    outputPlaceholders["ProcedureName"] = sp.Name;
                    outputPlaceholders["Name"] = sp.Name;
                    outputPlaceholders["Properties"] = resultSet.Columns.Select(c => new Dictionary<string, object>
                    {
                        ["Name"] = c.Name,
                        ["Type"] = MapClrType(c),
                        ["Description"] = c.Name,
                        ["Attribute"] = NeedsStringLengthAttribute(c) ? $"[StringLength({GetMaxLength(c)})] " : string.Empty
                    }).ToList();

                    var outputTemplate = await _templateEngine.GetProcessedTemplateAsync(
                        TemplateType.OutputModel,
                        targetFramework,
                        outputPlaceholders);

                    var outputFile = Path.Combine(schemaOutputsPath, sp.Name + "Result.cs");
                    await Output.WriteAsync(outputFile, outputTemplate.GetText(), isDryRun);
                    ConsoleService.Verbose($"Generated output model: {outputFile}");
                }

                // Entity model(s) from each result set
                if (sp.ResultSets?.Any() == true)
                {
                    var index = 1;
                    foreach (var rs in sp.ResultSets)
                    {
                        if (rs.Columns?.Any() != true) continue;
                        var entityPlaceholders = CreateBasePlaceholders();
                        entityPlaceholders["Schema"] = schema.Name;
                        entityPlaceholders["Source"] = sp.Name + (sp.ResultSets.Count > 1 ? $"[Set{index}]" : "");
                        entityPlaceholders["Name"] = sp.Name + (sp.ResultSets.Count > 1 ? $"Set{index}" : "Entity");
                        entityPlaceholders["Properties"] = rs.Columns.Select(c => new Dictionary<string, object>
                        {
                            ["Name"] = c.Name,
                            ["Type"] = MapClrType(c)
                        }).ToList();

                        var entityTemplate = await _templateEngine.GetProcessedTemplateAsync(
                            TemplateType.EntityModel,
                            targetFramework,
                            entityPlaceholders);

                        var entityFileName = sp.Name + (sp.ResultSets.Count > 1 ? $"Set{index}" : "Entity") + ".cs";
                        var entityFile = Path.Combine(schemaModelsPath, entityFileName);
                        await Output.WriteAsync(entityFile, entityTemplate.GetText(), isDryRun);
                        ConsoleService.Verbose($"Generated entity model: {entityFile}");
                        index++;
                    }
                }
            }
        }
    }

    private async Task GenerateTableTypeModelsAsync(
        string outputPath,
        TargetFrameworkEnum targetFramework,
        bool isDryRun)
    {
        var schemas = _metadataProvider.GetSchemas().Where(s => s.Status == SchemaStatusEnum.Build);
        foreach (var schema in schemas)
        {
            var schemaTableTypesPath = Path.Combine(outputPath, "TableTypes", schema.Name);
            if (!isDryRun) Directory.CreateDirectory(schemaTableTypesPath);

            foreach (var tt in schema.TableTypes ?? Enumerable.Empty<TableTypeModel>())
            {
                if (tt.Columns?.Any() != true) continue;
                var tableTypePlaceholders = CreateBasePlaceholders();
                tableTypePlaceholders["Schema"] = schema.Name;
                tableTypePlaceholders["Name"] = tt.Name;
                tableTypePlaceholders["Columns"] = tt.Columns.Select(c => new Dictionary<string, object>
                {
                    ["Name"] = c.Name,
                    ["Type"] = MapClrType(c)
                }).ToList();

                var tableTypeTemplate = await _templateEngine.GetProcessedTemplateAsync(
                    TemplateType.TableType,
                    targetFramework,
                    tableTypePlaceholders);

                var ttFile = Path.Combine(schemaTableTypesPath, tt.Name + "TableType.cs");
                await Output.WriteAsync(ttFile, tableTypeTemplate.GetText(), isDryRun);
                ConsoleService.Verbose($"Generated table type: {ttFile}");
            }
        }
    }

    private static string MapClrType(dynamic column)
    {
        // Vereinfachtes Mapping: Grundtyp bestimmen, Nullability einmalig anhängen
        try
        {
            var raw = column.DataType?.ToString() ?? column.Type?.ToString() ?? "string";
            var dbType = raw.ToLowerInvariant();
            var baseType = dbType switch
            {
                "int" => "int",
                "bigint" => "long",
                "smallint" => "short",
                "tinyint" => "byte",
                "bit" => "bool",
                "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
                "float" => "double",
                "real" => "float",
                "date" or "datetime" or "datetime2" or "smalldatetime" => "DateTime",
                "datetimeoffset" => "DateTimeOffset",
                "time" => "TimeSpan",
                "uniqueidentifier" => "Guid",
                "binary" or "varbinary" or "image" => "byte[]",
                "json" => "string",
                _ => "string"
            };
            
            bool isNullable = column.IsNullable == true;
            return isNullable
                ? $"{baseType}?"
                : baseType;
        }
        catch { return "string"; }
    }

    private static bool NeedsStringLengthAttribute(dynamic column)
    {
        try
        {
            var typeName = (column.DataType?.ToString() ?? column.Type?.ToString() ?? string.Empty).ToLowerInvariant();
            if (!typeName.StartsWith("nvarchar")) return false;
            var max = GetMaxLength(column);
            return max > 0;
        }
        catch { return false; }
    }

    private static int GetMaxLength(dynamic column)
    {
        try
        {
            // MaxLength property may be named MaxLength or max_length depending on source
            if (column == null) return -1;
            var t = column.GetType();
            var prop = t.GetProperty("MaxLength") ?? t.GetProperty("max_length");
            if (prop == null) return -1;
            var val = prop.GetValue(column);
            if (val is int i) return i;
            return -1;
        }
        catch { return -1; }
    }

    private Dictionary<string, object> CreateBasePlaceholders()
    {
        return new Dictionary<string, object>
        {
            ["Namespace"] = ConfigFile.Config.Project.Output.Namespace,
            // In modern mode die Konfig ignorieren; bleibt wegen Abwärtskompatibilität falls jemand net9 modern erzwingt.
            ["ConnectionStringName"] = IsModern(ConfigFile.Config.TargetFramework)
                ? "DefaultConnection"
                : ConfigFile.Config.Project.DataBase.RuntimeConnectionStringIdentifier ?? "DefaultConnection",
            ["ProjectName"] = "SpocR" // TODO: Add to configuration
        };
    }

    private static bool IsModern(string tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm) || !tfm.StartsWith("net")) return false;
        var core = tfm.Substring(3).Split('.')[0];
        return int.TryParse(core, out var major) && major >= 10;
    }

    private TargetFrameworkEnum GetTargetFramework()
    {
        return ConfigFile.Config.TargetFramework switch
        {
            "net9.0" => TargetFrameworkEnum.Net90,
            "net8.0" => TargetFrameworkEnum.Net80,
            "net6.0" => TargetFrameworkEnum.Net60,
            _ => Constants.DefaultTargetFramework
        };
    }

    private string GetModernOutputPath()
    {
        var basePath = ConfigFile.Config.Project.Output.DataContext.Path;
        return Path.Combine(DirectoryUtils.GetWorkingDirectory(basePath), "Modern");
    }

    private List<object> GetStoredProceduresForGeneration()
    {
        var storedProcedures = new List<object>();
        var schemas = _metadataProvider.GetSchemas().Where(s => s.Status == SchemaStatusEnum.Build);

        foreach (var schema in schemas)
        {
            foreach (var sp in schema.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>())
            {
                storedProcedures.Add(new
                {
                    Name = sp.Name,
                    Schema = schema.Name,
                    FullName = $"[{schema.Name}].[{sp.Name}]",
                    KebabCaseName = sp.Name.ToKebabCase(),
                    HasInputs = sp.Input?.Any() == true,
                    HasResults = sp.ResultSets?.Any() == true,
                    HasOutputs = false // TODO: Add output parameter detection
                });
            }
        }

        return storedProcedures;
    }

    private List<object> GetStoredProceduresForSchema(string schemaName)
    {
        var schema = _metadataProvider.GetSchemas().FirstOrDefault(s => s.Name == schemaName);
        if (schema?.StoredProcedures == null) return new List<object>();

        return schema.StoredProcedures.Select(sp => new
        {
            Name = sp.Name,
            Schema = schemaName,
            FullName = $"[{schemaName}].[{sp.Name}]",
            KebabCaseName = sp.Name.ToKebabCase(),
            HasInputs = sp.Input?.Any() == true,
            HasResults = sp.ResultSets?.Any() == true,
            HasOutputs = false // TODO: Add output parameter detection
        }).Cast<object>().ToList();
    }
}

/// <summary>
/// Extension methods for string conversion
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Converts PascalCase to kebab-case
    /// </summary>
    public static string ToKebabCase(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new System.Text.StringBuilder();
        result.Append(char.ToLowerInvariant(input[0]));

        for (int i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]))
            {
                result.Append('-');
                result.Append(char.ToLowerInvariant(input[i]));
            }
            else
            {
                result.Append(input[i]);
            }
        }

        return result.ToString();
    }
}
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
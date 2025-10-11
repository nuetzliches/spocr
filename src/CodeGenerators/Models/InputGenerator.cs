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
                : GetClrTypeNameFromSqlDbTypeName(item.SqlTypeName, item.IsNullable ?? false);

            // Property-/Parametername (PascalCase beibehalten)
            var propertyName = GetPropertyFromSqlInputTableType(item.Name);

            string attr = null;
            if (!isTableType
                && (item.SqlTypeName?.Equals(System.Data.SqlDbType.NVarChar.ToString(), StringComparison.InvariantCultureIgnoreCase) ?? false)
                && item.MaxLength.HasValue)
            {
                // Für Strings StringLength statt MaxLength verwenden
                needsDataAnnotations = true;
                attr = $"[property: StringLength({item.MaxLength.Value})] ";
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
        var compat = ConfigFile.Config.Project.Output?.CompatibilityMode;
        var legacyActive = string.Equals(compat, "v4.5", StringComparison.OrdinalIgnoreCase);
        // Emit class for legacy path (compat) or non-modern targets; record for modern unified SpocR
        var modernTfm = IsModernTfm(ConfigFile.Config.TargetFramework);
        var useRecord = modernTfm && !legacyActive;
        string file;
        if (useRecord)
        {
            file = string.Join('\n', usingLines) + (usingLines.Count > 0 ? "\n\n" : string.Empty)
                 + $"namespace {nsName};\n\n"
                 + $"public record {className}(\n{paramBlock}\n);\n";
        }
        else
        {
            // Backwards compatible class with auto-properties
            var props = string.Join("\n        ", paramSegments.Select(p =>
            {
                // p may start with one or multiple attribute blocks: [Attr] [Attr2] Type Name
                var working = p.Trim();
                var attributesCollected = new List<string>();
                while (working.StartsWith("["))
                {
                    var end = working.IndexOf(']');
                    if (end < 0) break; // malformed
                    var attr = working.Substring(0, end + 1);
                    attributesCollected.Add(attr);
                    working = working[(end + 1)..].TrimStart();
                }
                var lastSpace = working.LastIndexOf(' ');
                if (lastSpace <= 0)
                {
                    // No proper split, just return it as a public property
                    return $"public {working} {{ get; set; }}";
                }
                var typePart = working.Substring(0, lastSpace).Trim();
                var propName = working[(lastSpace + 1)..].Trim();
                for (int i = 0; i < attributesCollected.Count; i++)
                {
                    attributesCollected[i] = attributesCollected[i]
                        .Replace("[property:", "[", StringComparison.OrdinalIgnoreCase)
                        .Trim();
                    // Collapse any '[  Attribute' -> '[Attribute'
                    attributesCollected[i] = System.Text.RegularExpressions.Regex.Replace(attributesCollected[i], @"\[\s+", "[");
                }
                string attrsBlock = attributesCollected.Count > 0
                    ? string.Join("\n    ", attributesCollected)
                    : null;
                if (attrsBlock != null)
                {
                    return $"{attrsBlock}\n        public {typePart} {propName} {{ get; set; }}";
                }
                return $"public {typePart} {propName} {{ get; set; }}";
            }));
            // Build constructors
            // Extract property signatures again (type + name) without attributes
            var ctorParams = new List<string>();
            var assignments = new List<string>();
            foreach (var segment in paramSegments)
            {
                var work = segment.Trim();
                while (work.StartsWith("["))
                {
                    var end = work.IndexOf(']');
                    if (end < 0) break;
                    work = work[(end + 1)..].TrimStart();
                }
                var lastSpace = work.LastIndexOf(' ');
                if (lastSpace <= 0) continue;
                var typePart = work.Substring(0, lastSpace).Trim();
                var propName = work[(lastSpace + 1)..].Trim();
                var paramName = char.ToLowerInvariant(propName[0]) + propName.Substring(1);
                ctorParams.Add($"{typePart} {paramName}");
                assignments.Add($"            {propName} = {paramName};");
            }
            var fullCtor = ctorParams.Count > 0
                ? $"        public {className}({string.Join(", ", ctorParams)})\n        {{\n{string.Join("\n", assignments)}\n        }}\n\n"
                : string.Empty;
            var parameterlessCtor = string.Empty; // removed obsolete empty ctor
            file = string.Join('\n', usingLines) + (usingLines.Count > 0 ? "\n\n" : string.Empty)
                 + $"namespace {nsName}\n{{\n    public class {className}\n    {{\n{parameterlessCtor}{fullCtor}        {props}\n    }}\n}}\n";
        }

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

        var useNewLayout = IsModernTfm(ConfigFile.Config.TargetFramework) && !string.Equals(ConfigFile.Config.Project.Output?.CompatibilityMode, "v4.5", StringComparison.OrdinalIgnoreCase);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures;
            if (!storedProcedures.Any()) continue;
            string schemaPath;
            if (useNewLayout)
            {
                // New layout: SpocR/[schema]/<ProcName>Input.cs (flat per schema directory)
                var root = DirectoryUtils.GetWorkingDirectory("SpocR");
                schemaPath = Path.Combine(root, schema.Path);
            }
            else
            {
                var dataContextInputPath = DirectoryUtils.GetWorkingDirectory(
                    ConfigFile.Config.Project.Output.DataContext.Path,
                    ConfigFile.Config.Project.Output.DataContext.Inputs.Path);
                schemaPath = Path.Combine(dataContextInputPath, schema.Path);
            }
            if (!Directory.Exists(schemaPath) && !isDryRun)
            {
                Directory.CreateDirectory(schemaPath);
            }

            foreach (var sp in storedProcedures)
            {
                if (!sp.HasInputs()) continue;
                var fileName = useNewLayout ? $"{sp.Name}Input.cs" : $"{sp.Name}.cs";
                var fileNameWithPath = Path.Combine(schemaPath, fileName);
                var sourceText = await GetInputTextForStoredProcedureAsync(schema, sp);
                if (useNewLayout && sourceText != null)
                {
                    var rootNs = ConfigFile.Config.Project.Output.Namespace ?? "SpocR.Generated";
                    if (rootNs.EndsWith(".DataContext", StringComparison.OrdinalIgnoreCase)) rootNs = rootNs[..^11];
                    var modernNs = $"namespace {rootNs}.SpocR.{schema.Name};";
                    var txt = sourceText.ToString();
                    // Replace first namespace line (legacy DataContext pattern) with modern
                    var replacedOnce = false;
                    txt = System.Text.RegularExpressions.Regex.Replace(txt, @"namespace\s+.+?;", m =>
                    {
                        if (replacedOnce) return m.Value; // leave others
                        replacedOnce = true;
                        return modernNs;
                    });
                    sourceText = Microsoft.CodeAnalysis.Text.SourceText.From(txt);
                }
                await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

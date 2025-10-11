using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.CodeGenerators.Utils;

/// <summary>
/// Manages loading and processing code templates for the generator classes
/// </summary>
public class TemplateManager
{
    private readonly OutputService _output;
    private readonly FileManager<ConfigurationModel> _configManager;
    private readonly Dictionary<string, CompilationUnitSyntax> _templateCache = [];

    private readonly string[] _templateTypes = {
        "Inputs/Input.cs",
        "Models/Model.cs",
        "Outputs/Output.cs",
        "TableTypes/TableType.cs",
        "StoredProcedures/StoredProcedureExtensions.cs"
    };

    /// <summary>
    /// Creates a new instance of the TemplateManager
    /// </summary>
    public TemplateManager(OutputService output, FileManager<ConfigurationModel> configManager)
    {
        _output = output;
        _configManager = configManager;
        PreloadTemplates();
    }

    private void PreloadTemplates()
    {
        // Modern Mode (net10+ without legacy compatibility): uses embedded / dynamic templates – skip legacy preload
        if (IsModernEffective())
        {
            return; // kein Preload nötig
        }
        var rootDir = _output.GetOutputRootDir();

        foreach (var templateType in _templateTypes)
        {
            var templatePath = Path.Combine(rootDir.FullName, "DataContext", templateType);
            if (File.Exists(templatePath))
            {
                var fileContent = File.ReadAllText(templatePath);
                var tree = CSharpSyntaxTree.ParseText(fileContent);
                _templateCache[templateType] = tree.GetCompilationUnitRoot();
            }
        }
    }

    /// <summary>
    /// Loads a template and performs basic namespace and class name replacements
    /// </summary>
    public async Task<CompilationUnitSyntax> GetProcessedTemplateAsync(string templateType, string schemaName, string className)
    {
        bool compatibility = string.Equals(_configManager.Config.Project.Output?.CompatibilityMode, "v4.5", StringComparison.OrdinalIgnoreCase);
        // Modern Mode (no compatibility): dynamic templates
        if (IsModernEffective() && !compatibility)
        {
            // Modern Layout (net10+): unified namespace <RootNs>.SpocR[.<Schema>] regardless of segment
            var rootNs = _configManager.Config.Project.Output.Namespace ?? "SpocR.Generated";
            // Strip trailing .DataContext if legacy value present
            if (rootNs.EndsWith(".DataContext", StringComparison.OrdinalIgnoreCase))
                rootNs = rootNs[..^11];
            var schemaPart = string.IsNullOrWhiteSpace(schemaName) ? string.Empty : $".{schemaName}";
            var targetNs = $"{rootNs}.SpocR{schemaPart}";

            string code;
            // We only need lightweight class shells; specialized SP generation handled elsewhere.
            code = $@"namespace {targetNs};\npublic class {className} {{\n}}";
            var treeStub = CSharpSyntaxTree.ParseText(code);
            return treeStub.GetCompilationUnitRoot();
        }
        // Legacy or compatibility: load from versioned Output* template source
        if (!_templateCache.TryGetValue(templateType, out var template))
        {
            var rootDir = _output.GetOutputRootDir();
            var templatePath = Path.Combine(rootDir.FullName, "DataContext", templateType.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException($"Legacy template '{templateType}' not found at '{templatePath}'.");
            }
            var fileContent = await File.ReadAllTextAsync(templatePath);
            var tree = CSharpSyntaxTree.ParseText(fileContent);
            template = tree.GetCompilationUnitRoot();
        }

        var root = template;
        var templateFileName = Path.GetFileNameWithoutExtension(templateType);

        // Replace Namespace (explicit construction to avoid partial replacement edge cases)
        var configuredRootNs = _configManager.Config.Project.Output.Namespace?.Trim();
        if (string.IsNullOrWhiteSpace(configuredRootNs))
        {
            throw new System.InvalidOperationException("Configuration error: 'Project.Output.Namespace' is missing or empty in spocr.json. Please provide a valid root namespace.");
        }

        // Determine root segment (Models / Inputs / Outputs / TableTypes / StoredProcedures) based on template path
        string nsSegment = "Models"; // default
        if (templateType.StartsWith("Inputs/", StringComparison.OrdinalIgnoreCase)) nsSegment = "Inputs";
        else if (templateType.StartsWith("Outputs/", StringComparison.OrdinalIgnoreCase)) nsSegment = "Outputs";
        else if (templateType.StartsWith("TableTypes/", StringComparison.OrdinalIgnoreCase)) nsSegment = "TableTypes";
        else if (templateType.StartsWith("StoredProcedures/", StringComparison.OrdinalIgnoreCase)) nsSegment = "StoredProcedures"; // usually extensions

        // Build namespace; if schemaName is empty (root-level artifacts like Outputs base files) avoid trailing dot
        string targetNamespace;
        var endsWithDataContext = configuredRootNs.EndsWith(".DataContext", StringComparison.OrdinalIgnoreCase);
#pragma warning disable CS0618 // suppress deprecated Role.Kind usage until refactor
        if (_configManager.Config.Project.Role.Kind == RoleKindEnum.Lib)
        {
            targetNamespace = string.IsNullOrWhiteSpace(schemaName)
                ? $"{configuredRootNs}.{nsSegment}"
                : $"{configuredRootNs}.{nsSegment}.{schemaName}";
        }
        else if (endsWithDataContext)
        {
            // User provided root already contains DataContext suffix; do not append again
            targetNamespace = string.IsNullOrWhiteSpace(schemaName)
                ? $"{configuredRootNs}.{nsSegment}"
                : $"{configuredRootNs}.{nsSegment}.{schemaName}";
        }
        else
        {
            targetNamespace = string.IsNullOrWhiteSpace(schemaName)
                ? $"{configuredRootNs}.DataContext.{nsSegment}"
                : $"{configuredRootNs}.DataContext.{nsSegment}.{schemaName}";
        }

        // Safety: collapse any accidental duplicate dots (should not happen if config is valid)
        while (targetNamespace.Contains(".."))
            targetNamespace = targetNamespace.Replace("..", ".");

        root = root.ReplaceNamespace(_ => targetNamespace);

        // Replace ClassName
        root = root.ReplaceClassName(ci => ci.Replace(templateFileName, className));

        return root;
    }

    private static bool IsModern(string tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm) || !tfm.StartsWith("net")) return false;
        var core = tfm.Substring(3).Split('.')[0];
        return int.TryParse(core, out var major) && major >= 10;
    }

    private bool IsModernEffective()
    {
        if (!IsModern(_configManager.Config.TargetFramework)) return false;
        // Honor legacy compatibility mode even on modern TFM
        return !string.Equals(_configManager.Config.Project?.Output?.CompatibilityMode, "v4.5", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a template file from the embedded templates folder (modern unified raw access).
    /// Falls back to disk relative to generator output directory if not embedded.
    /// </summary>
    public async Task<string> ReadTemplateRawAsync(string templateFileName)
    {
        // Look for file under src/Templates first (development scenario)
        // We intentionally do not cache raw text to allow iterative edits during development.
        // Provide minimal safety against path traversal.
        if (templateFileName.IndexOf("..", StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Template file traversal detected.");

        // Attempt relative path resolution from current working directory
        var candidates = new List<string>();
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            candidates.Add(Path.Combine(cwd, "src", "Templates", templateFileName));
            candidates.Add(Path.Combine(cwd, "Templates", templateFileName));
        }
        catch { /* ignore cwd issues */ }

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path);
            }
        }

        throw new FileNotFoundException($"Unified template '{templateFileName}' not found in expected locations.");
    }

    /// <summary>
    /// Removes the first property from a class (the template placeholder property)
    /// </summary>
    public static CompilationUnitSyntax RemoveTemplateProperty(CompilationUnitSyntax root)
    {
        // Support both file-scoped and block namespaces
        ClassDeclarationSyntax classNode = null;
        if (root.Members[0] is FileScopedNamespaceDeclarationSyntax fns)
        {
            classNode = fns.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classNode == null) return root;
            var filtered = classNode.Members.Where(m =>
                m is not PropertyDeclarationSyntax p ||
                !p.GetLeadingTrivia().ToString().Contains("<spocr-placeholder-property>") &&
                !p.ToFullString().Contains("<spocr-placeholder-property>")
            ).ToList();
            if (filtered.Count == classNode.Members.Count) // fallback: remove first property if marker missing
            {
                filtered = classNode.Members.Skip(1).ToList();
            }
            var newClass = classNode.WithMembers(SyntaxFactory.List(filtered));
            return root.ReplaceNode(classNode, newClass);
        }
        else if (root.Members[0] is NamespaceDeclarationSyntax bns)
        {
            classNode = bns.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classNode == null) return root;
            var filtered = classNode.Members.Where(m =>
                m is not PropertyDeclarationSyntax p ||
                !p.GetLeadingTrivia().ToString().Contains("<spocr-placeholder-property>") &&
                !p.ToFullString().Contains("<spocr-placeholder-property>")
            ).ToList();
            if (filtered.Count == classNode.Members.Count)
            {
                filtered = classNode.Members.Skip(1).ToList();
            }
            var newClass = classNode.WithMembers(SyntaxFactory.List(filtered));
            return root.ReplaceNode(classNode, newClass);
        }
        return root;
    }

    /// <summary>
    /// Creates a using directive based on the project type
    /// </summary>
    public UsingDirectiveSyntax CreateImportForNamespace(string importNamespace, string suffix = null)
    {
        string fullNamespace;

        if (_configManager.Config.Project.Role.Kind == RoleKindEnum.Lib)
        {
            fullNamespace = $"{_configManager.Config.Project.Output.Namespace}.{importNamespace}";
        }
        else
        {
            fullNamespace = $"{_configManager.Config.Project.Output.Namespace}.DataContext.{importNamespace}";
        }

        if (!string.IsNullOrEmpty(suffix))
        {
            fullNamespace += $".{suffix}";
        }

        return SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(fullNamespace));
    }

    /// <summary>
    /// Creates a using directive for a table type schema
    /// </summary>
    public UsingDirectiveSyntax CreateTableTypeImport(string tableTypeSchema, SchemaModel tableTypeSchemaConfig)
    {
        // is schema of table type ignored and its an extension?
        var useFromLib = tableTypeSchemaConfig?.Status != SchemaStatusEnum.Build
            && _configManager.Config.Project.Role.Kind == RoleKindEnum.Extension;

        if (useFromLib)
        {
            return SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(
                $"{_configManager.Config.Project.Role.LibNamespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
        }
        else if (_configManager.Config.Project.Role.Kind == RoleKindEnum.Lib)
#pragma warning restore CS0618
        {
            return SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(
                $"{_configManager.Config.Project.Output.Namespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
        }
        else
        {
            return SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(
                $"{_configManager.Config.Project.Output.Namespace}.DataContext.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
        }
    }

    /// <summary>
    /// Generates the final source text from a root element
    /// </summary>
    public static SourceText GenerateSourceText(CompilationUnitSyntax root)
    {
        return root.NormalizeWhitespace().GetText();
    }
}

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
        if (!_templateCache.TryGetValue(templateType, out var template))
        {
            // Fallback to loading directly from disk if it is not in the cache
            var rootDir = _output.GetOutputRootDir();
            var templatePath = Path.Combine(rootDir.FullName, "DataContext", templateType);
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

        string targetNamespace = _configManager.Config.Project.Role.Kind == RoleKindEnum.Lib
            ? $"{configuredRootNs}.Models.{schemaName}"
            : $"{configuredRootNs}.DataContext.Models.{schemaName}";

        // Safety: collapse any accidental duplicate dots (should not happen if config is valid)
        while (targetNamespace.Contains(".."))
            targetNamespace = targetNamespace.Replace("..", ".");

        root = root.ReplaceNamespace(_ => targetNamespace);

        // Replace ClassName
        root = root.ReplaceClassName(ci => ci.Replace(templateFileName, className));

        return root;
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

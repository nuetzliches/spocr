using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// Verwaltet das Laden und Verarbeiten von Code-Templates für die Generator-Klassen
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
    /// Erstellt eine neue Instanz des TemplateManager
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
    /// Lädt ein Template und führt grundlegende Namespace- und Klassenname-Ersetzungen durch
    /// </summary>
    public CompilationUnitSyntax GetProcessedTemplate(string templateType, string schemaName, string className)
    {
        if (!_templateCache.TryGetValue(templateType, out var template))
        {
            // Fallback zum direkten Laden, falls nicht im Cache
            var rootDir = _output.GetOutputRootDir();
            var templatePath = Path.Combine(rootDir.FullName, "DataContext", templateType);
            var fileContent = File.ReadAllText(templatePath);
            var tree = CSharpSyntaxTree.ParseText(fileContent);
            template = tree.GetCompilationUnitRoot();
        }

        var root = template;
        var templateFileName = Path.GetFileNameWithoutExtension(templateType);

        // Replace Namespace
        if (_configManager.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", _configManager.Config.Project.Output.Namespace).Replace("Schema", schemaName));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", _configManager.Config.Project.Output.Namespace).Replace("Schema", schemaName));
        }

        // Replace ClassName
        root = root.ReplaceClassName(ci => ci.Replace(templateFileName, className));

        return root;
    }

    /// <summary>
    /// Entfernt die erste Property aus einer Klasse (Template-Property)
    /// </summary>
    public static CompilationUnitSyntax RemoveTemplateProperty(CompilationUnitSyntax root)
    {
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        var newClassMembers = SyntaxFactory.List(classNode.Members.Skip(1));
        var newClassNode = classNode.WithMembers(newClassMembers);
        return root.ReplaceNode(classNode, newClassNode);
    }

    /// <summary>
    /// Erzeugt einen Using-Import basierend auf dem Projekttyp
    /// </summary>
    public UsingDirectiveSyntax CreateImportForNamespace(string importNamespace, string suffix = null)
    {
        string fullNamespace;

        if (_configManager.Config.Project.Role.Kind == ERoleKind.Lib)
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
    /// Erzeugt eine Using-Direktive für ein TableType-Schema
    /// </summary>
    public UsingDirectiveSyntax CreateTableTypeImport(string tableTypeSchema, SchemaModel tableTypeSchemaConfig)
    {
        // is schema of table type ignored and its an extension?
        var useFromLib = tableTypeSchemaConfig?.Status != SchemaStatusEnum.Build
            && _configManager.Config.Project.Role.Kind == ERoleKind.Extension;

        if (useFromLib)
        {
            return SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(
                $"{_configManager.Config.Project.Role.LibNamespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
        }
        else if (_configManager.Config.Project.Role.Kind == ERoleKind.Lib)
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
    /// Generiert den finalen Source-Text aus einem Root-Element
    /// </summary>
    public static SourceText GenerateSourceText(CompilationUnitSyntax root)
    {
        return root.NormalizeWhitespace().GetText();
    }
}

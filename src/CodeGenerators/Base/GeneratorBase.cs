using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpocR.Contracts;
using SpocR.DataContext;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Base;

/// <summary>
/// Basis-Klasse für alle Code-Generatoren
/// </summary>
public abstract class GeneratorBase
{
    protected readonly FileManager<ConfigurationModel> ConfigFile;
    protected readonly OutputService Output;
    protected readonly IConsoleService ConsoleService;

    public GeneratorBase(FileManager<ConfigurationModel> configFile, OutputService output, IConsoleService consoleService)
    {
        ConfigFile = configFile;
        Output = output;
        ConsoleService = consoleService;
    }

    #region Type Helpers

    protected TypeSyntax ParseTypeFromSqlDbTypeName(string sqlTypeName, bool isNullable)
    {
        // temporary for #56: we should not abort execution if config is corrupt
        if (string.IsNullOrEmpty(sqlTypeName))
        {
            ConsoleService.PrintCorruptConfigMessage($"Could not parse 'SqlTypeName' - setting the type to dynamic");
            sqlTypeName = "Variant";
        }

        sqlTypeName = sqlTypeName.Split('(')[0];
        var sqlType = Enum.Parse<System.Data.SqlDbType>(sqlTypeName, true);
        var clrType = SqlDbHelper.GetType(sqlType, isNullable);
        return SyntaxFactory.ParseTypeName(clrType.ToGenericTypeString());
    }

    protected static string GetIdentifierFromSqlInputTableType(string name)
    {
        name = $"{name[1..].FirstCharToLower()}";
        var reservedKeyWords = new[] { "params", "namespace" };
        if (reservedKeyWords.Contains(name))
        {
            name = $"@{name}";
        }
        return name;
    }

    protected static string GetPropertyFromSqlInputTableType(string name)
    {
        name = $"{name[1..].FirstCharToUpper()}";
        return name;
    }

    protected static string GetTypeNameForTableType(Definition.TableType tableType)
    {
        return $"{tableType.Name}";
    }

    protected static TypeSyntax GetTypeSyntaxForTableType(StoredProcedureInputModel input)
    {
        return input.Name.EndsWith("List")
            ? SyntaxFactory.ParseTypeName($"IEnumerable<{input.TableTypeName}>")
            : SyntaxFactory.ParseTypeName($"{input.TableTypeName}");
    }

    #endregion

    #region Template Processing

    /// <summary>
    /// Erstellt ein Verzeichnis für einen Schema-Pfad, falls es noch nicht existiert
    /// </summary>
    protected string EnsureDirectoryExists(string basePath, string subPath, string schemaPath, bool isDryRun)
    {
        var fullPath = Path.Combine(DirectoryUtils.GetWorkingDirectory(basePath, subPath), schemaPath);
        if (!Directory.Exists(fullPath) && !isDryRun)
        {
            Directory.CreateDirectory(fullPath);
        }
        return fullPath;
    }

    /// <summary>
    /// Fügt Imports für TableTypes zu einer Compilation-Unit hinzu
    /// </summary>
    protected CompilationUnitSyntax AddMultipleTableTypeImports(CompilationUnitSyntax root, IEnumerable<string> tableTypeSchemas)
    {
        foreach (var tableTypeSchema in tableTypeSchemas)
        {
            root = AddTableTypeImport(root, tableTypeSchema);
        }
        return root;
    }

    /// <summary>
    /// Fügt einen Using-Import für eine TableType-Schema hinzu
    /// </summary>
    protected CompilationUnitSyntax AddTableTypeImport(CompilationUnitSyntax root, string tableTypeSchema)
    {
        var tableTypeSchemaConfig = ConfigFile.Config.Schema.Find(s => s.Name.Equals(tableTypeSchema));
        // is schema of table type ignored and its an extension?
        var useFromLib = tableTypeSchemaConfig?.Status != SchemaStatusEnum.Build
            && ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Extension;

        var paramUsingDirective = useFromLib
                            ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                            : ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib
                                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
        return root.AddUsings(paramUsingDirective);
    }

    /// <summary>
    /// Erzeugt einen generischen Using-Import basierend auf dem Projekt-Typ und Namespace
    /// </summary>
    protected UsingDirectiveSyntax CreateImportDirective(string importNamespace, string suffix = null)
    {
        string fullNamespace;

        if (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib)
        {
            fullNamespace = $"{ConfigFile.Config.Project.Output.Namespace}.{importNamespace}";
        }
        else
        {
            fullNamespace = $"{ConfigFile.Config.Project.Output.Namespace}.DataContext.{importNamespace}";
        }

        if (!string.IsNullOrEmpty(suffix))
        {
            fullNamespace += $".{suffix}";
        }

        return SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(fullNamespace));
    }

    /// <summary>
    /// Generiert den finalen Quelltext von einer Root-Syntax
    /// </summary>
    protected static SourceText GenerateSourceText(CompilationUnitSyntax root)
    {
        return root.NormalizeWhitespace().GetText();
    }

    /// <summary>
    /// Fügt eine Property zu einer Klasse hinzu
    /// </summary>
    protected static CompilationUnitSyntax AddProperty(
        CompilationUnitSyntax root,
        ref ClassDeclarationSyntax classNode,
        PropertyDeclarationSyntax propertyNode)
    {
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = classNode.AddMembers(propertyNode);
        nsNode = nsNode.ReplaceNode(nsNode.Members[0], classNode);
        return root.ReplaceNode(root.Members[0], nsNode);
    }

    /// <summary>
    /// Erzeugt eine Property mit optionalen Attributen
    /// </summary>
    protected static PropertyDeclarationSyntax CreatePropertyWithAttributes(
        TypeSyntax type,
        string name,
        Dictionary<string, object> attributeValues = null)
    {
        // Property erstellen
        var property = SyntaxFactory.PropertyDeclaration(type, name)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddAccessorListAccessors(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

        // Attribute hinzufügen
        if (attributeValues != null && attributeValues.Count > 0)
        {
            var attributeList = SyntaxFactory.AttributeList();
            foreach (var attr in attributeValues)
            {
                var attribute = SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName(attr.Key),
                    SyntaxFactory.ParseAttributeArgumentList($"({attr.Value})"));

                attributeList = attributeList.AddAttributes(attribute);
            }
            property = property.AddAttributeLists(attributeList.NormalizeWhitespace());
        }

        return property;
    }

    #endregion

    /// <summary>
    /// Generiert ein Verzeichnis-Schema und gibt alle zugehörigen Stored Procedures zurück
    /// </summary>
    protected IEnumerable<(Definition.Schema Schema, IEnumerable<Definition.StoredProcedure> StoredProcedures, string Path)>
        GenerateSchemaDirectoriesAndGetProcedures(string basePath, string subPath, bool requireInputs = false, bool isDryRun = false)
    {
        var schemas = ConfigFile.Config.Schema
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false));

        var definitionSchemas = schemas.Select(Definition.ForSchema).ToList();

        foreach (var schema in definitionSchemas)
        {
            var storedProcedures = schema.StoredProcedures;

            if (requireInputs)
            {
                storedProcedures = storedProcedures.Where(sp => sp.HasInputs()).ToList();
            }

            if (!storedProcedures.Any())
            {
                continue;
            }

            var dataContextPath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, subPath);
            var path = Path.Combine(dataContextPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            yield return (schema, storedProcedures, path);
        }
    }
}

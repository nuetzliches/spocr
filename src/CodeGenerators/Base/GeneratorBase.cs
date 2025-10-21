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
/// Base class for all code generators
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

        // Graceful fallback for explicit unresolved JSON column marker introduced by vNext snapshot ('unknown').
        // We intentionally map this placeholder to NVARCHAR and treat it as a string so generation can proceed.
        // Downstream enrichment may later upgrade the SqlTypeName to a concrete value; logging kept verbose to avoid warning spam.
        if (string.Equals(sqlTypeName, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleService.Verbose("[type-fallback] SqlTypeName 'unknown' mapped to NVARCHAR/string (pending enrichment upgrade).");
            sqlTypeName = System.Data.SqlDbType.NVarChar.ToString();
        }

        // SQL Server besitzt keinen nativen JSON Typ; Snapshot kann nevertheless 'json' als Marker liefern.
        // Mappe diesen Marker defensiv auf NVARCHAR damit Enum.Parse nicht fehlschlägt und die Generation fortsetzen kann.
        if (string.Equals(sqlTypeName, "json", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleService.Verbose("[type-fallback] SqlTypeName 'json' mapped to NVARCHAR/string (no native SQL Server JSON type)." );
            sqlTypeName = System.Data.SqlDbType.NVarChar.ToString();
        }

        // SQL Server alias 'rowversion' (synonym zu deprecated 'timestamp') ist kein SqlDbType Enum Name ('Timestamp' existiert nur als historisches Konzept).
        // Mappe auf Binary, da die CLR-Repräsentation ohnehin byte[] ist.
        if (string.Equals(sqlTypeName, "rowversion", StringComparison.OrdinalIgnoreCase) || string.Equals(sqlTypeName, "timestamp", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleService.Verbose("[type-fallback] SqlTypeName 'rowversion' mapped to VARBINARY (byte[]) for generation.");
            sqlTypeName = System.Data.SqlDbType.VarBinary.ToString();
        }

        sqlTypeName = sqlTypeName.Split('(')[0];
        var sqlType = Enum.Parse<System.Data.SqlDbType>(sqlTypeName, true);
        var clrType = SqlDbHelper.GetType(sqlType, isNullable);
        return SyntaxFactory.ParseTypeName(clrType.ToGenericTypeString());
    }

    protected static string GetIdentifierFromSqlInputTableType(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var working = name.StartsWith("@") && name.Length > 1 ? name[1..] : name;
        name = working.FirstCharToLower();
        var reservedKeyWords = new[] { "params", "namespace" };
        if (reservedKeyWords.Contains(name))
        {
            name = $"@{name}";
        }
        return name;
    }

    protected static string GetPropertyFromSqlInputTableType(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var working = name.StartsWith("@") && name.Length > 1 ? name[1..] : name;
        return working.FirstCharToUpper();
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
    /// Creates a directory for a schema path if it does not exist
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
    /// Adds imports for table types to a compilation unit
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
    /// Adds a using directive for a table type schema
    /// </summary>
    protected CompilationUnitSyntax AddTableTypeImport(CompilationUnitSyntax root, string tableTypeSchema)
    {
        var tableTypeSchemaConfig = ConfigFile.Config.Schema.Find(s => s.Name.Equals(tableTypeSchema));
        // Entscheidungslogik:
        // 1. Lib-Projekte: <OutputNs>.TableTypes.<Schema>
        // 2. Nicht-Lib (Default/Extension) Build-Schema: <OutputNs>.DataContext.TableTypes.<Schema>
        // 3. Extension + Nicht-Build-Schema (wird aus Lib konsumiert): <LibNamespace>.TableTypes.<Schema>
        bool isBuild = tableTypeSchemaConfig?.Status == SchemaStatusEnum.Build;
#pragma warning disable CS0618
        bool isExtension = ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Extension;
        bool isLib = ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib;
#pragma warning restore CS0618

        UsingDirectiveSyntax paramUsingDirective;
        if (isLib)
        {
            paramUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
            return root.AddUsings(paramUsingDirective);
        }

        if (isExtension)
        {
            // Für Extensions ausschliesslich das lokale DataContext TableTypes Namespace importieren
            var localUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
            if (!root.Usings.Any(u => u.Name.ToString() == localUsing.Name.ToString()))
            {
                root = root.AddUsings(localUsing);
            }
            return root;
        }

        // Default (nicht Lib, nicht Extension)
        paramUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
        if (!root.Usings.Any(u => u.Name.ToString() == paramUsingDirective.Name.ToString()))
        {
            root = root.AddUsings(paramUsingDirective);
        }
        return root;
    }

    /// <summary>
    /// Creates a generic using directive based on the project type and namespace
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
    /// Generates the final source text from a root syntax
    /// </summary>
    protected static SourceText GenerateSourceText(CompilationUnitSyntax root)
    {
        return root.NormalizeWhitespace().GetText();
    }

    /// <summary>
    /// Adds a property to a class
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
    /// Creates a property with optional attributes
    /// </summary>
    protected static PropertyDeclarationSyntax CreatePropertyWithAttributes(
        TypeSyntax type,
        string name,
        Dictionary<string, object> attributeValues = null)
    {
        // Create property
        var property = SyntaxFactory.PropertyDeclaration(type, name)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddAccessorListAccessors(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

        // Add attributes
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
    /// Generates a directory schema and returns all related stored procedures
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

using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpocR.CodeGenerators.Base;
using SpocR.CodeGenerators.Utils;
using SpocR.Contracts;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

public class TableTypeGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService,
    TemplateManager templateManager,
    ISchemaMetadataProvider metadataProvider
) : GeneratorBase(configFile, output, consoleService)
{
    public async Task<SourceText> GetTableTypeTextAsync(Definition.Schema schema, Definition.TableType tableType)
    {
        // Load and process the template with the template manager
        var root = await templateManager.GetProcessedTemplateAsync("TableTypes/TableType.cs", schema.Name, GetTypeNameForTableType(tableType));

        // If its an extension, add usings for the lib
        if (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Extension)
        {
            var libModelUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}.TableTypes"));
            root = root.AddUsings(libModelUsingDirective).NormalizeWhitespace();
        }

        // Unterstützt file-scoped und block namespaces
        SyntaxNode nsNodeRaw = root.Members[0];
        ClassDeclarationSyntax classNode;
        if (nsNodeRaw is FileScopedNamespaceDeclarationSyntax fns)
        {
            classNode = fns.Members.OfType<ClassDeclarationSyntax>().First();
        }
        else if (nsNodeRaw is NamespaceDeclarationSyntax bns)
        {
            classNode = bns.Members.OfType<ClassDeclarationSyntax>().First();
        }
        else
        {
            throw new InvalidOperationException("Unexpected namespace syntax node kind in table type template.");
        }

        // Create Properties
        if (tableType.Columns != null)
        {
            foreach (var column in tableType.Columns)
            {
                nsNodeRaw = root.Members[0];
                if (nsNodeRaw is FileScopedNamespaceDeclarationSyntax fnsLoop)
                    classNode = fnsLoop.Members.OfType<ClassDeclarationSyntax>().First();
                else if (nsNodeRaw is NamespaceDeclarationSyntax bnsLoop)
                    classNode = bnsLoop.Members.OfType<ClassDeclarationSyntax>().First();
                // Falls Template keine Beispiel-Property enthält, synthetisch eine minimale erstellen
                PropertyDeclarationSyntax propertyNode;
                if (classNode.Members.OfType<PropertyDeclarationSyntax>().Any())
                {
                    propertyNode = classNode.Members.OfType<PropertyDeclarationSyntax>().First();
                }
                else
                {
                    propertyNode = SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName("string"), "_Stub")
                        .AddModifiers(SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword))
                        .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(new [] {
                            SyntaxFactory.AccessorDeclaration(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SemicolonToken)),
                            SyntaxFactory.AccessorDeclaration(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SemicolonToken))
                        })));
                }

                var propertyIdentifier = SyntaxFactory.ParseToken($" {column.Name} ");

                propertyNode = propertyNode
                    .WithType(ParseTypeFromSqlDbTypeName(column.SqlTypeName, column.IsNullable ?? false));

                propertyNode = propertyNode
                    .WithIdentifier(propertyIdentifier);

                // Add Attribute for NVARCHAR with MaxLength
                if (column.SqlTypeName.Equals(SqlDbType.NVarChar.ToString(), StringComparison.InvariantCultureIgnoreCase)
                    && column.MaxLength.HasValue)
                {
                    var attributes = propertyNode.AttributeLists.Add(
                        SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                            SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("MaxLength"), SyntaxFactory.ParseAttributeArgumentList($"({column.MaxLength})"))
                        )).NormalizeWhitespace());

                    propertyNode = propertyNode.WithAttributeLists(attributes);
                }

                root = root.AddProperty(ref classNode, propertyNode);
            }
        }

        // Remove template placeholder property
        root = TemplateManager.RemoveTemplateProperty(root);

        // Convert body-properties into positional record parameters (no blank lines between parameters)
        var typeName = GetTypeNameForTableType(tableType);
        var text = TemplateManager.GenerateSourceText(root).ToString();

        try
        {
            // Ensure record keyword
            text = System.Text.RegularExpressions.Regex.Replace(text,
                $@"public\s+(partial\s+)?class\s+{System.Text.RegularExpressions.Regex.Escape(typeName)}",
                match => match.Value.Replace("class", "record"));

            // Extract property lines inside declaration
            var declPattern = $@"public\s+(?:partial\s+)?record\s+{System.Text.RegularExpressions.Regex.Escape(typeName)}\s*\{{(?<body>[\s\S]*?)\}}";
            var declMatch = System.Text.RegularExpressions.Regex.Match(text, declPattern);
            if (declMatch.Success)
            {
                var body = declMatch.Groups["body"].Value;
                var propPattern = @"(?:\[MaxLength\((?<len>\d+)\)\]\s*)?\s*public\s+(?<type>[^\s]+(?:\s*<[^>]+>)?\??)\s+(?<name>\w+)\s*\{\s*get;\s*set;\s*\}";
                var props = System.Text.RegularExpressions.Regex.Matches(body, propPattern);
                var segments = new System.Collections.Generic.List<string>();
                foreach (System.Text.RegularExpressions.Match pm in props)
                {
                    var type = pm.Groups["type"].Value.Trim();
                    var name = pm.Groups["name"].Value.Trim();
                    var len = pm.Groups["len"].Success ? pm.Groups["len"].Value : null;
                    var attr = len != null ? $"[property: MaxLength({len})] " : string.Empty;
                    segments.Add($"{attr}{type} {name}");
                }
                // Fallback: build from metadata if regex didn't find any properties
                if (segments.Count == 0 && tableType.Columns != null)
                {
                    foreach (var col in tableType.Columns)
                    {
                        var typeSyntax2 = ParseTypeFromSqlDbTypeName(col.SqlTypeName, col.IsNullable ?? false).ToString();
                        string attr2 = null;
                        if (col.SqlTypeName.Equals(SqlDbType.NVarChar.ToString(), StringComparison.InvariantCultureIgnoreCase) && col.MaxLength.HasValue)
                        {
                            attr2 = $"[property: MaxLength({col.MaxLength.Value})] ";
                        }
                        segments.Add($"{attr2}{typeSyntax2} {col.Name}".TrimStart());
                    }
                }
                var paramBlock = segments.Count > 0 ? ("    " + string.Join(",\n    ", segments)) : string.Empty;
                var newDecl = $"public record {typeName}(\n{paramBlock}\n) : ITableType;";
                text = text.Substring(0, declMatch.Index) + newDecl + text.Substring(declMatch.Index + declMatch.Length);
            }
        }
        catch { }

        return SourceText.From(text);
    }

    public async Task GenerateDataContextTableTypesAsync(bool isDryRun)
    {
        // Ensure ITableType interface (template: ITableType.base.cs) is materialized once into DataContext/TableTypes root (drop .base)
        try
        {
            var tableTypesRoot = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.TableTypes.Path);
            if (!Directory.Exists(tableTypesRoot) && !isDryRun)
            {
                Directory.CreateDirectory(tableTypesRoot);
            }
            var interfaceTemplatePath = Path.Combine(Output.GetOutputRootDir().FullName, "DataContext", "TableTypes", "ITableType.base.cs");
            var interfaceTargetPath = Path.Combine(tableTypesRoot, "ITableType.cs");
            if (File.Exists(interfaceTemplatePath))
            {
                if (!File.Exists(interfaceTargetPath))
                {
                    var raw = await File.ReadAllTextAsync(interfaceTemplatePath);
                    // Replace Source.DataContext with configured namespace (root, no schema segment)
                    var configuredRootNs = ConfigFile.Config.Project.Output.Namespace?.Trim();
                    if (string.IsNullOrWhiteSpace(configuredRootNs)) throw new InvalidOperationException("Missing Project.Output.Namespace");
                    var targetNs = ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib
                        ? $"{configuredRootNs}.TableTypes"
                        : $"{configuredRootNs}.DataContext.TableTypes";
                    raw = raw.Replace("namespace Source.DataContext.TableTypes", $"namespace {targetNs}");
                    await Output.WriteAsync(interfaceTargetPath, SourceText.From(raw), isDryRun);
                }
            }
        }
        catch (Exception itx)
        {
            ConsoleService.Verbose($"[tabletypes] Skipped ITableType generation: {itx.Message}");
        }

        var schemas = metadataProvider.GetSchemas()
            .Where(i => i.TableTypes?.Any() ?? false)
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var tableTypes = schema.TableTypes;

            var dataContextTableTypesPath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.TableTypes.Path);
            var path = Path.Combine(dataContextTableTypesPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var tableType in tableTypes)
            {
                var fileName = $"{tableType.Name}TableType.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = await GetTableTypeTextAsync(schema, tableType);

                await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

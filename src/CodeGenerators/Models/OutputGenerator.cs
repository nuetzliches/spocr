using System;
using System.Collections.Generic;
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
using SpocR.Roslyn.Helpers;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

/// <summary>
/// Re-introduced generator for DataContext/Outputs. Creates <StoredProcedureName>Output classes
/// for each stored procedure that declares one or more OUTPUT (or INPUT/OUTPUT) parameters.
/// </summary>
public class OutputGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService,
    TemplateManager templateManager,
    ISchemaMetadataProvider metadataProvider
) : GeneratorBase(configFile, output, consoleService)
{
    public async Task GenerateDataContextOutputsAsync(bool isDryRun)
    {
        // Ensure single Outputs.cs (merging previous base/partial approach)
        try
        {
            var outputsRoot = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Outputs.Path);
            if (!Directory.Exists(outputsRoot) && !isDryRun)
            {
                Directory.CreateDirectory(outputsRoot);
            }

            var outputsFilePath = Path.Combine(outputsRoot, "Outputs.cs");
            var templatePath = File.Exists(Path.Combine(outputsRoot, "Outputs.base.cs")) ? "Outputs/Outputs.base.cs" : "Outputs/Outputs.base.cs"; // fallback always the same for now
            var outputsTemplate = await templateManager.GetProcessedTemplateAsync(templatePath, string.Empty, "Outputs");
            await Output.WriteAsync(outputsFilePath, TemplateManager.GenerateSourceText(outputsTemplate), isDryRun);

            // Remove legacy files if present
            var legacyBase = Path.Combine(outputsRoot, "Outputs.base.cs");
            if (File.Exists(legacyBase) && !isDryRun)
            {
                try { File.Delete(legacyBase); ConsoleService.Verbose("[outputs] Removed legacy Outputs.base.cs"); } catch { /* ignore */ }
            }
            var legacyPartial = Path.Combine(outputsRoot, "Outputs.partial.cs");
            if (File.Exists(legacyPartial) && !isDryRun)
            {
                try { File.Delete(legacyPartial); ConsoleService.Verbose("[outputs] Removed legacy Outputs.partial.cs"); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            ConsoleService.Warn($"[outputs] Failed ensuring Outputs.cs file: {ex.Message}");
        }

        // Iterate schemas that are in build scope and actually have SPs with output params
        var schemas = metadataProvider.GetSchemas()
            .Where(s => s.Status == SchemaStatusEnum.Build && (s.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema)
            .ToList();

        foreach (var schema in schemas)
        {
            var spWithOutputs = schema.StoredProcedures
                .Where(sp => sp.Input.Any(i => i.IsOutput))
                .ToList();
            if (!spWithOutputs.Any()) continue;

            var path = EnsureDirectoryExists(
                ConfigFile.Config.Project.Output.DataContext.Path,
                ConfigFile.Config.Project.Output.DataContext.Outputs.Path,
                schema.Path,
                isDryRun);

            foreach (var sp in spWithOutputs)
            {
                var outputParams = sp.Input.Where(i => i.IsOutput).ToList();
                if (!outputParams.Any()) continue; // safety

                // Diagnostic logging block
                try
                {
                    var paramList = string.Join(", ", outputParams.Select(o => o.Name + ":" + o.SqlTypeName + (o.IsNullable == true ? "?" : string.Empty)));
                    var customCount = outputParams.Count(o => !string.Equals(o.Name, "@ResultId", StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(o.Name, "@RecordId", StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(o.Name, "@RowVersion", StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(o.Name, "@Result", StringComparison.OrdinalIgnoreCase));
                    ConsoleService.Verbose($"[outputs-diag] {sp.SqlObjectName} outputs=({paramList}) custom={customCount}");
                }
                catch { /* ignore diagnostics */ }

                var className = sp.Name + "Output";
                var fileName = className + ".cs";
                var filePath = Path.Combine(path, fileName);

                var root = await templateManager.GetProcessedTemplateAsync("Outputs/Output.cs", schema.Name, className);

                // Access namespace + class
                var nsNode = root.Members[0] as BaseNamespaceDeclarationSyntax;
                if (nsNode == null)
                {
                    ConsoleService.Warn($"[outputs] Template root for {className} missing namespace – skipping.");
                    continue;
                }
                var classNode = nsNode.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (classNode == null)
                {
                    ConsoleService.Warn($"[outputs] Template class for {className} missing – skipping.");
                    continue;
                }

                // Fix base type: template had 'class Output : Output' which after rename becomes 'FooOutput : FooOutput'. Replace self inheritance with base 'Output'.
                if (classNode.BaseList != null)
                {
                    var bases = classNode.BaseList.Types;
                    if (bases.Count == 1 && bases[0].Type is IdentifierNameSyntax id && id.Identifier.Text == className)
                    {
                        classNode = classNode.WithBaseList(
                            SyntaxFactory.BaseList(
                                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                                    SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName("Output"))
                                )));
                    }
                }
                else
                {
                    classNode = classNode.WithBaseList(
                        SyntaxFactory.BaseList(
                            SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                                SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName("Output"))
                            )));
                }

                // Remove placeholder property first, then establish baseline AFTER removal
                var firstProp = classNode.Members.OfType<PropertyDeclarationSyntax>().FirstOrDefault();
                if (firstProp != null)
                {
                    classNode = classNode.RemoveNode(firstProp, SyntaxRemoveOptions.KeepNoTrivia);
                }
                var baselineMemberCount = classNode.Members.OfType<PropertyDeclarationSyntax>().Count();

                // Add properties for each OUTPUT parameter (skip standard base ones)
                var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "ResultId", "RecordId", "RowVersion", "Result" };
                foreach (var p in outputParams)
                {
                    var propName = p.Name.TrimStart('@').FirstCharToUpper();
                    if (skipNames.Contains(propName)) continue;
                    if (classNode.Members.OfType<PropertyDeclarationSyntax>().Any(m => m.Identifier.Text == propName)) continue;
                    // Nullability: Für OUTPUT-Parameter war bisher der Fallback (?? true) -> alle unbekannten wurden nullable generiert.
                    // Das führte dazu, dass z.B. 'TransitionRowVersion' (IsNullable = false oder fehlend) als 'long?' ausgegeben wurde.
                    // Neue Regel: Nur nullable generieren, wenn Metadaten explizit IsNullable == true liefern. Fallback ist false.
                    var typeSyntax = ParseTypeFromSqlDbTypeName(p.SqlTypeName, p.IsNullable == true);
                    var prop = SyntaxFactory.PropertyDeclaration(typeSyntax, SyntaxFactory.Identifier(propName))
                        .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                        .AddAccessorListAccessors(
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                        );
                    classNode = classNode.AddMembers(prop);
                }

                var finalMemberCount = classNode.Members.OfType<PropertyDeclarationSyntax>().Count();
                var addedPropertyCount = finalMemberCount - baselineMemberCount;
                var hasCustomOutputs = outputParams.Any(p => !skipNames.Contains(p.Name.TrimStart('@').FirstCharToUpper()));

                // With Option B: Always emit DTO if there is ANY custom output param (hasCustomOutputs)
                if (!hasCustomOutputs)
                {
                    ConsoleService.Verbose($"[outputs] Skipped default-only output model {className}");
                    continue;
                }
                // Log if anomaly (custom outputs but zero added props)
                if (hasCustomOutputs && addedPropertyCount <= 0)
                {
                    ConsoleService.Warn($"[outputs] Detected custom outputs for {className} but no properties added (baseline={baselineMemberCount}, final={finalMemberCount}). Emitting empty class.");
                }

                // Add XML header only if we keep the file
                var header = "/// <summary>Auto-generated OUTPUT model for stored procedure '" + sp.SqlObjectName + "'.</summary>" + Environment.NewLine +
                             "/// <remarks>Generated by SpocR – do not edit manually.</remarks>" + Environment.NewLine;
                if (!classNode.GetLeadingTrivia().ToFullString().Contains("Auto-generated OUTPUT model"))
                {
                    classNode = classNode.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(header).AddRange(classNode.GetLeadingTrivia()));
                }

                var updatedNs = nsNode.ReplaceNode(nsNode.Members[0], classNode);
                root = root.ReplaceNode(nsNode, updatedNs);

                if (!isDryRun)
                {
                    await Output.WriteAsync(filePath, TemplateManager.GenerateSourceText(root), isDryRun);
                    ConsoleService.Verbose($"[outputs] Generated {className} ({addedPropertyCount} property/ies)");
                }
            }
        }

        // Cleanup empty schema directories (contain no *.cs except preserved base partials) under Outputs
        try
        {
            var baseOutputsDir = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Outputs.Path);
            if (Directory.Exists(baseOutputsDir))
            {
                foreach (var schemaDir in Directory.GetDirectories(baseOutputsDir, "*", SearchOption.TopDirectoryOnly))
                {
                    var csFiles = Directory.GetFiles(schemaDir, "*.cs", SearchOption.TopDirectoryOnly)
                        .Where(f => !f.EndsWith("Outputs.cs", StringComparison.OrdinalIgnoreCase) && !f.EndsWith("Outputs.base.cs", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (csFiles.Count == 0)
                    {
                        // If only the schema dir plus maybe nothing else -> remove it entirely
                        Directory.Delete(schemaDir, true);
                        ConsoleService.Verbose($"[outputs-cleanup] Removed empty outputs schema folder '{new DirectoryInfo(schemaDir).Name}'");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleService.Warn($"[outputs-cleanup] Failed: {ex.Message}");
        }
    }
}

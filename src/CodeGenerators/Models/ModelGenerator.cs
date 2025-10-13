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
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

public class ModelGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService,
    TemplateManager templateManager,
    ISchemaMetadataProvider metadataProvider
) : GeneratorBase(configFile, output, consoleService)
{
    public async Task<SourceText> GetModelTextForStoredProcedureAsync(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        // Load template
        var root = await templateManager.GetProcessedTemplateAsync("Models/Model.cs", schema.Name, storedProcedure.Name);
        var nsBase = root.Members[0] as BaseNamespaceDeclarationSyntax;
        if (nsBase == null) throw new System.InvalidOperationException("Template must contain a namespace.");
        var classNode = nsBase.Members.OfType<ClassDeclarationSyntax>().First();
        var templateProperty = classNode.Members.OfType<PropertyDeclarationSyntax>().First();

        // If the procedure returns no ResultSets at all we now SKIP generating a model entirely to avoid empty classes.
        if (storedProcedure.ResultSets == null || storedProcedure.ResultSets.Count == 0)
            return null;
        if (storedProcedure.ResultSets.Count != 1)
        {
            throw new System.InvalidOperationException($"Model generation expects exactly one ResultSet (got {storedProcedure.ResultSets?.Count ?? 0}) for '{storedProcedure.Name}'.");
        }
        var currentSet = storedProcedure.ResultSets[0];
        var resultColumns = currentSet?.Columns?.ToList() ?? [];
        var hasResultColumns = resultColumns.Any();

        // Heuristic: Legacy FOR JSON output (single synthetic column) -> treat as raw JSON
        // Detection: exactly one column, name = JSON_F52E2B61-18A1-11d1-B105-00805F49916B (case-insensitive), nvarchar(max)
        var currentSetReturnsJson = currentSet?.ReturnsJson ?? false;
        // Legacy single-column FOR JSON heuristic removed. Rely solely on parser FOR JSON detection.
        var treatAsJson = currentSetReturnsJson;

        // Local helpers
        string InferType(string sqlType, bool? nullable)
        {
            if (string.IsNullOrWhiteSpace(sqlType)) return "string";
            return ParseTypeFromSqlDbTypeName(sqlType, nullable ?? true).ToString();
        }

        ClassDeclarationSyntax AddProperty(ClassDeclarationSyntax cls, string name, string typeName)
        {
            // Build a fresh auto-property instead of cloning the template placeholder (its marker triggers removal)
            var identifier = SyntaxFactory.Identifier(name.FirstCharToUpper());
            var typeSyntax = SyntaxFactory.ParseTypeName(typeName);
            var prop = SyntaxFactory.PropertyDeclaration(typeSyntax, identifier)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                );
            return cls.AddMembers(prop);
        }

        // Removed legacy BuildNestedClass (replaced by JsonPath tree generation)

        if (hasResultColumns && !treatAsJson)
        {
            foreach (var col in resultColumns)
            {
                if (string.IsNullOrWhiteSpace(col.Name)) continue;
                classNode = AddProperty(classNode, col.Name, InferType(col.SqlTypeName, col.IsNullable));
            }
        }
        else if (treatAsJson && resultColumns.Any())
        {
            // Build a tree structure from JsonPath segments so we can generate nested classes
            var rootNode = new JsonPathNode("__root__");
            foreach (var col in resultColumns)
            {
                var path = string.IsNullOrWhiteSpace(col.JsonPath) ? col.Name : col.JsonPath;
                if (string.IsNullOrWhiteSpace(path)) continue;
                var segments = path.Split('.', System.StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0) continue;
                var node = rootNode;
                for (int i = 0; i < segments.Length; i++)
                {
                    var seg = segments[i];
                    var isLeaf = i == segments.Length - 1;
                    node = node.GetOrAdd(seg);
                    if (isLeaf)
                    {
                        node.Columns.Add(col); // attach column at leaf
                    }
                }
            }

            // Generate nested classes recursively; collect top-level segment names for root properties.
            var generatedClasses = new System.Collections.Generic.Dictionary<string, ClassDeclarationSyntax>(System.StringComparer.OrdinalIgnoreCase);

            const string NestedClassSuffix = "Sub"; // configurable if needed later
            ClassDeclarationSyntax GenerateClass(JsonPathNode node)
            {
                // For nested nodes (excluding synthetic root) add suffix to minimize collisions with similarly named properties.
                var className = node.Name == "__root__" ? "Root" : node.Name.FirstCharToUpper() + NestedClassSuffix;
                if (generatedClasses.TryGetValue(className, out var existing)) return existing;
                var cls = SyntaxFactory.ClassDeclaration(className).AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                // Child classes first
                foreach (var child in node.Children.Values)
                {
                    // Flatten rule: if child has exactly one column, no grandchildren, and column name == child name -> emit property only
                    if (!child.HasChildren && child.Columns.Count == 1 && string.Equals(child.Columns[0].Name, child.Name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        var col = child.Columns[0];
                        var propName = col.Name.FirstCharToUpper();
                        if (!cls.Members.OfType<PropertyDeclarationSyntax>().Any(p => p.Identifier.Text == propName))
                        {
                            var typeName = InferType(col.SqlTypeName, col.IsNullable);
                            var prop = SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName(typeName), SyntaxFactory.Identifier(propName))
                                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                                .AddAccessorListAccessors(
                                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                                );
                            cls = cls.AddMembers(prop);
                        }
                        continue; // don't generate child class wrapper
                    }

                    var childCls = GenerateClass(child);
                    if (!cls.Members.OfType<ClassDeclarationSyntax>().Any(c => c.Identifier.Text == childCls.Identifier.Text))
                    {
                        cls = cls.AddMembers(childCls);
                    }
                }
                // Leaf columns -> properties
                foreach (var c in node.Columns)
                {
                    var propName = c.Name.FirstCharToUpper();
                    if (!cls.Members.OfType<PropertyDeclarationSyntax>().Any(p => p.Identifier.Text == propName))
                    {
                        var typeName = InferType(c.SqlTypeName, c.IsNullable);
                        var prop = SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName(typeName), SyntaxFactory.Identifier(propName))
                            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                            .AddAccessorListAccessors(
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                            );
                        cls = cls.AddMembers(prop);
                    }
                }
                generatedClasses[className] = cls;
                return cls;
            }

            // Add nested classes + root properties with flattening rule:
            // If a top-level segment has exactly one leaf column and no children AND that column's JsonPath has no dot => direct property (flatten)
            // Else generate nested class tree.
            foreach (var top in rootNode.Children.Values)
            {
                var singleLeafNoChildren = !top.HasChildren && top.Columns.Count == 1 && !(top.Columns[0].JsonPath?.Contains('.') ?? false);
                if (singleLeafNoChildren)
                {
                    var col = top.Columns[0];
                    classNode = AddProperty(classNode, col.Name, InferType(col.SqlTypeName, col.IsNullable));
                    continue;
                }

                // For deeper or grouped nodes generate classes.
                var cls = GenerateClass(top);
                // Remove redundant self-wrapping pattern: class Currency { class Code { string Code; } } -> flatten inner if it mirrors parent name only
                cls = SimplifySingleSelfWrapping(cls);
                if (!classNode.Members.OfType<ClassDeclarationSyntax>().Any(c => c.Identifier.Text == cls.Identifier.Text))
                {
                    classNode = classNode.AddMembers(cls);
                }
                var desiredPropName = top.Name.FirstCharToUpper();
                if (desiredPropName == cls.Identifier.Text)
                {
                    // If class name equals desired property name (rare after suffix) shorten property (remove suffix)
                    desiredPropName = top.Name.FirstCharToUpper();
                }
                if (!classNode.Members.OfType<PropertyDeclarationSyntax>().Any(p => p.Identifier.Text == desiredPropName))
                {
                    classNode = AddProperty(classNode, desiredPropName, cls.Identifier.Text);
                }
            }
        }

        // Remove template placeholder property
        root = TemplateManager.RemoveTemplateProperty(root.ReplaceNode(nsBase, nsBase.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(classNode))));

        // Insert standardized auto-generated header on class
        var autoHeader = "/// <summary>Auto-generated by SpocR. DO NOT EDIT. Changes will be overwritten on rebuild.</summary>" + System.Environment.NewLine +
                 "/// <remarks>Generated at " + System.DateTime.UtcNow.ToString("u") + "</remarks>" + System.Environment.NewLine;
        // Hinweis: Timestamp bleibt bewusst bestehen für Transparenz, wird aber beim UpToDate-Vergleich
        // in OutputService.WriteAsync mittels Regex normalisiert, um unnötigen Churn zu vermeiden.
        var nsAfter = (BaseNamespaceDeclarationSyntax)root.Members[0];
        var clsAfter = nsAfter.Members.OfType<ClassDeclarationSyntax>().First();
        if (!clsAfter.GetLeadingTrivia().ToFullString().Contains("Auto-generated"))
        {
            var updated = clsAfter.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(autoHeader).AddRange(clsAfter.GetLeadingTrivia()));
            root = root.ReplaceNode(clsAfter, updated);
        }

        if (!hasResultColumns && currentSetReturnsJson)
        {
            consoleService.Warn($"No JSON columns extracted for stored procedure '{storedProcedure.Name}'. Generated empty model (RawJson fallback).");
            // Add RawJson fallback property to surface payload
            classNode = AddProperty(classNode, "RawJson", "string");
            // Replace class in root to persist new property
            var nsAfterRemoval = root.Members.OfType<BaseNamespaceDeclarationSyntax>().First();
            var existingClass = nsAfterRemoval.Members.OfType<ClassDeclarationSyntax>().First();
            root = root.ReplaceNode(existingClass, classNode);
            // Ensure placeholder property removed if template left it in during replacement
            root = TemplateManager.RemoveTemplateProperty(root);
            // Add doc comment if still empty
            classNode = root.Members.OfType<BaseNamespaceDeclarationSyntax>().First().Members.OfType<ClassDeclarationSyntax>().First();
            if (!classNode.Members.OfType<PropertyDeclarationSyntax>().Any())
            {
                var xml = "/// <summary>Generated JSON model (no columns detected at generation time). The underlying stored procedure returns JSON, but its column structure couldn't be statically inferred.</summary>" + System.Environment.NewLine +
                          "/// <remarks>Consider rewriting the procedure with an explicit SELECT list or stable aliases so properties can be generated.</remarks>" + System.Environment.NewLine;
                var updated = classNode.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(xml).AddRange(classNode.GetLeadingTrivia()));
                var currentNs = (BaseNamespaceDeclarationSyntax)root.Members[0];
                root = root.ReplaceNode(classNode, updated);
            }
        }

        return TemplateManager.GenerateSourceText(root);
    }

    private sealed class JsonPathNode
    {
        public string Name { get; }
        public System.Collections.Generic.Dictionary<string, JsonPathNode> Children { get; } = new(System.StringComparer.OrdinalIgnoreCase);
        public System.Collections.Generic.List<StoredProcedureContentModel.ResultColumn> Columns { get; } = new();
        public bool HasChildren => Children.Count > 0;
        public JsonPathNode(string name) { Name = name; }
        public JsonPathNode GetOrAdd(string name)
        {
            if (!Children.TryGetValue(name, out var node))
            {
                node = new JsonPathNode(name);
                Children[name] = node;
            }
            return node;
        }
    }

    private static ClassDeclarationSyntax SimplifySingleSelfWrapping(ClassDeclarationSyntax cls)
    {
        // Pattern: class Currency { class Code { string Code; } } produced from path Currency.Code.Code (name duplication)
        // Or more generally: child class with same single property name as itself - keep as-is unless it only wraps one property identical to parent logic.
        // For now keep logic minimal: if a nested class has EXACTLY one property and NO further nested classes, and the property name equals the class name, promote property to parent level
        var updated = cls;
        var childClasses = cls.Members.OfType<ClassDeclarationSyntax>().ToList();
        foreach (var child in childClasses)
        {
            var grandChildren = child.Members.OfType<ClassDeclarationSyntax>();
            if (grandChildren.Any()) continue; // skip deeper structures
            var props = child.Members.OfType<PropertyDeclarationSyntax>().ToList();
            if (props.Count == 1 && string.Equals(props[0].Identifier.Text, child.Identifier.Text, System.StringComparison.Ordinal))
            {
                // Flatten: add property to parent with child name and remove child class
                if (!updated.Members.OfType<PropertyDeclarationSyntax>().Any(p => p.Identifier.Text == props[0].Identifier.Text))
                {
                    updated = updated.AddMembers(props[0]);
                }
                updated = updated.RemoveNode(child, SyntaxRemoveOptions.KeepNoTrivia);
            }
        }
        return updated;
    }

    private static ClassDeclarationSyntax EnsureClassPath(ClassDeclarationSyntax root, string[] pathSegments)
    {
        if (pathSegments.Length == 0) return root;
        ClassDeclarationSyntax updatedRoot = root;
        ClassDeclarationSyntax currentRoot = root;
        for (int i = 0; i < pathSegments.Length; i++)
        {
            var seg = pathSegments[i].FirstCharToUpper();
            var existing = currentRoot.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.Text == seg);
            if (existing == null)
            {
                existing = SyntaxFactory.ClassDeclaration(seg).AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                currentRoot = currentRoot.AddMembers(existing);
                // replace in parent tree
                updatedRoot = (ClassDeclarationSyntax)new SimpleNestedReplaceRewriter(seg, currentRoot).Visit(updatedRoot) ?? currentRoot;
            }
            currentRoot = existing;
        }
        return updatedRoot;
    }

    private static ClassDeclarationSyntax AddLeafProperty(ClassDeclarationSyntax root, string[] fullSegments, string typeName)
    {
        var leafName = fullSegments.Last().FirstCharToUpper();
        var containerPath = fullSegments.Take(fullSegments.Length - 1).Select(s => s.FirstCharToUpper()).ToArray();
        var container = FindClass(root, containerPath);
        if (container == null) return root;
        if (!container.Members.OfType<PropertyDeclarationSyntax>().Any(p => p.Identifier.Text == leafName))
        {
            var prop = SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName(typeName), SyntaxFactory.Identifier(leafName))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
            var replaced = container.AddMembers(prop);
            root = (ClassDeclarationSyntax)new SimpleNestedReplaceRewriter(container.Identifier.Text, replaced).Visit(root) ?? root;
        }
        return root;
    }

    private static ClassDeclarationSyntax FindClass(ClassDeclarationSyntax root, string[] path)
    {
        if (path.Length == 0) return root;
        var current = root;
        foreach (var seg in path)
        {
            var next = current.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.Text == seg);
            if (next == null) return null;
            current = next;
        }
        return current;
    }

    private sealed class SimpleNestedReplaceRewriter : CSharpSyntaxRewriter
    {
        private readonly string _target;
        private readonly ClassDeclarationSyntax _replacement;
        public SimpleNestedReplaceRewriter(string target, ClassDeclarationSyntax replacement)
        { _target = target; _replacement = replacement; }
        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (node.Identifier.Text == _target)
                return _replacement;
            return base.VisitClassDeclaration(node);
        }
    }

    public async Task GenerateDataContextModels(bool isDryRun)
    {
        var schemas = metadataProvider.GetSchemas()
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures.ToList();

            if (storedProcedures.Count == 0)
            {
                continue;
            }

            var dataContextModelPath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Models.Path);
            var path = Path.Combine(dataContextModelPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var storedProcedure in storedProcedures)
            {
                var resultSets = storedProcedure.ResultSets;
                if (resultSets == null || resultSets.Count == 0)
                {
                    await WriteSingleModelAsync(schema, storedProcedure, path, isDryRun);
                    continue;
                }

                for (var rIndex = 0; rIndex < resultSets.Count; rIndex++)
                {
                    var modelName = rIndex == 0 ? storedProcedure.Name : storedProcedure.Name + "_" + rIndex;
                    // Always build a synthetic single-set StoredProcedureModel for clarity & symmetry
                    var spModel = new SpocR.Models.StoredProcedureModel(new SpocR.DataContext.Models.StoredProcedure
                    {
                        Name = modelName,
                        SchemaName = schema.Name
                    })
                    {
                        Content = new StoredProcedureContentModel
                        {
                            ResultSets = new[] { resultSets[rIndex] }
                        }
                    };
                    var modelSp = Definition.ForStoredProcedure(spModel, schema);
                    await WriteSingleModelAsync(schema, modelSp, path, isDryRun);
                }
            }
        }

        // Post-generation cleanup: remove empty schema folders in Models and Outputs (if any were created but no files written)
        try
        {
            if (!isDryRun)
            {
                var baseModelsDir = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Models.Path);
                if (Directory.Exists(baseModelsDir))
                {
                    foreach (var dir in Directory.GetDirectories(baseModelsDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly).Length == 0)
                        {
                            Directory.Delete(dir, true);
                            consoleService.Verbose($"[cleanup] Removed empty model folder '{new DirectoryInfo(dir).Name}'");
                        }
                    }
                }

                // Outputs directory may still exist from legacy; ensure we also remove accidental empty schema folders there.
                var outputsDir = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.Outputs.Path);
                if (Directory.Exists(outputsDir))
                {
                    foreach (var dir in Directory.GetDirectories(outputsDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly).Length == 0)
                        {
                            Directory.Delete(dir, true);
                            consoleService.Verbose($"[cleanup] Removed empty outputs folder '{new DirectoryInfo(dir).Name}'");
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            consoleService.Warn($"[cleanup] Could not remove empty model/output folders: {ex.Message}");
        }
    }

    private async Task WriteSingleModelAsync(Definition.Schema schema, Definition.StoredProcedure storedProcedure, string path, bool isDryRun)
    {
        if (storedProcedure.ResultSets == null || storedProcedure.ResultSets.Count == 0)
            return; // skip zero-result procedures completely
        if (storedProcedure.ResultSets.Count != 1)
        {
            throw new System.InvalidOperationException($"Model generation expects exactly one ResultSet (got {storedProcedure.ResultSets?.Count ?? 0}) for '{storedProcedure.Name}'.");
        }
        var currentSet = storedProcedure.ResultSets[0];
        var currentSetReturnsJson = currentSet.ReturnsJson;
        var hasResultCols = (currentSet.Columns?.Any() ?? false);
        var isScalarResultCols = hasResultCols && !currentSetReturnsJson && currentSet.Columns.Count == 1;
        if (!currentSetReturnsJson && isScalarResultCols)
            return; // skip scalar tabular model (true single-value), but not legacy FOR JSON payload

        var fileName = $"{storedProcedure.Name}.cs";
        var fileNameWithPath = Path.Combine(path, fileName);
        var sourceText = await GetModelTextForStoredProcedureAsync(schema, storedProcedure);
        if (sourceText != null)
            await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);
    }
}

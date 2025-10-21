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
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

public class StoredProcedureGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService,
    TemplateManager templateManager,
    ISchemaMetadataProvider metadataProvider
) : GeneratorBase(configFile, output, consoleService)
{
#pragma warning disable CS0618 // Suppress obsolete warnings for Role.Kind until removal in v5
    public async Task<SourceText> GetStoredProcedureExtensionsCodeAsync(Definition.Schema schema, List<Definition.StoredProcedure> storedProcedures)
    {
        // Entity grouping previously relied on OperationKind-derived EntityName. Fallback: use first procedure name as grouping key.
        var entityName = storedProcedures.First().Name;

        // Load and process the template with the template manager
        var root = await templateManager.GetProcessedTemplateAsync("StoredProcedures/StoredProcedureExtensions.cs", schema.Name, $"{entityName}Extensions");

        // If its an extension, add usings for the lib
        if (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Extension)
        {
            var libUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}"));
            root = root.AddUsings(libUsingDirective).NormalizeWhitespace();

            var libModelUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}.Models"));
            root = root.AddUsings(libModelUsingDirective).NormalizeWhitespace();

            var libOutputsUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}.Outputs"));
            root = root.AddUsings(libOutputsUsingDirective).NormalizeWhitespace();
        }
        else
        {
            // Previously conditional on Read/Write; now always adjust template usings.
            for (var i = 0; i < root.Usings.Count; i++)
            {
                var usingDirective = root.Usings[i];
                var newUsingName = ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib
                    ? SyntaxFactory.ParseName(usingDirective.Name.ToString().Replace("Source.DataContext", ConfigFile.Config.Project.Output.Namespace))
                    : SyntaxFactory.ParseName(usingDirective.Name.ToString().Replace("Source", ConfigFile.Config.Project.Output.Namespace));
                root = root.ReplaceNode(usingDirective, usingDirective.WithName(newUsingName));
            }
        }

        // Determine if any stored procedure in this group actually produces a model (skip pure scalar non-JSON procs)
        bool NeedsModel(Definition.StoredProcedure sp)
        {
            if (sp.ResultSets == null || sp.ResultSets.Count == 0) return false;
            // Primary set = first JSON result set if any, otherwise the first result set
            var primary = sp.ResultSets.FirstOrDefault(r => r.ReturnsJson) ?? sp.ResultSets.First();
            if (primary == null) return false;
            if (primary.ReturnsJson) return true; // JSON always implies a model (even if column count is 0 for deserialize)
            var cols = primary.Columns?.Count ?? 0;
            if (cols == 0) return false;
            if (cols == 1)
            {
                // Single non-JSON column -> only treat as model when not just a scalar nvarchar(max) pseudo CRUD value
                var c = primary.Columns[0];
                bool isNVarChar = (c.SqlTypeName?.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase) ?? false);
                // Wenn es weitere Sets gibt und eines JSON ist -> wir brauchen das Modell falls dieses Set nicht das JSON ist
                bool hasOtherJson = sp.ResultSets.Any(r => r != primary && r.ReturnsJson);
                if (isNVarChar && !hasOtherJson) return false; // rein skalar
            }
            return true;
        }

        var needsModelUsing = storedProcedures.Any(NeedsModel);
        if (needsModelUsing)
        {
            var flatten = string.IsNullOrWhiteSpace(ConfigFile.Config.Project.Output?.DataContext?.Path) || ConfigFile.Config.Project.Output.DataContext.Path.TrimEnd('/', '\\') == ".";
            var modelUsing = (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib || flatten)
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Models.{schema.Name}"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Models.{schema.Name}"));
            root = root.AddUsings(modelUsing).NormalizeWhitespace();
        }

        // Add Usings for Inputs
        if (storedProcedures.Any(s => s.HasInputs()))
        {
            var flatten = string.IsNullOrWhiteSpace(ConfigFile.Config.Project.Output?.DataContext?.Path) || ConfigFile.Config.Project.Output.DataContext.Path.TrimEnd('/', '\\') == ".";
            var inputUsingDirective = (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib || flatten)
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Inputs.{schema.Name}"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Inputs.{schema.Name}"));
            root = root.AddUsings(inputUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        // Add Usings for Outputs
        // Outputs namespace no longer needed after Output removal
        // (Removed) legacy Outputs namespace imports

        // Add Usings for TableTypes
        // If Inputs contains a TableType, add using for TableTypes
        var tableTypeSchemas = storedProcedures.SelectMany(sp => sp.Input.Where(i => i.IsTableType ?? false))
                             .GroupBy(t => t.TableTypeSchemaName, (key, group) => key).ToList();

        foreach (var tableTypeSchema in tableTypeSchemas)
        {
            root = AddTableTypeImport(root, tableTypeSchema);
        }

        // After table type imports, remove any remaining template Source.* usings
        var usings = root.Usings.Where(_ => !_.Name.ToString().StartsWith("Source."));
        root = root.WithUsings([.. usings]);

        // Conditionally add outputs usings (root + schema) if any proc has OUTPUT parameters
        // Normalisierte Skip-Liste (ohne '@'), damit uneinheitliche Metadaten-Namensgebung (mit/ohne '@') konsistent behandelt wird.
        var baseOutputSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ResultId", "RecordId", "RowVersion", "Result" };
        // Helper zur Normalisierung eines OUTPUT-Namens
        string NormalizeOutputName(string name) => (name ?? string.Empty).TrimStart('@');
        bool HasAnyOutputs = storedProcedures.Any(sp => sp.GetOutputs()?.Any() ?? false);
        bool HasCustomOutputs = storedProcedures.Any(sp => (sp.GetOutputs()?.Count(o => !baseOutputSkip.Contains(NormalizeOutputName(o.Name))) ?? 0) > 0);
        // Für Extension-Rollen werden zwar keine Bootstrap-Outputs.cs Dateien erzeugt, aber individuelle Output-Klassen (Schema) werden generiert.
        // Daher benötigen die StoredProcedure-Extensions auch bei Extension-Rollen ein using auf das lokale Schema-Outputs-Namespace, damit
        // z.B. OrganizationUpdateIsDeletedOutput aufgelöst wird.
#pragma warning disable CS0618
        if (HasAnyOutputs)
        {
            var flatten = string.IsNullOrWhiteSpace(ConfigFile.Config.Project.Output?.DataContext?.Path) || ConfigFile.Config.Project.Output.DataContext.Path.TrimEnd('/', '\\') == ".";
            // Root Outputs using (nur wenn nicht Extension ODER falls wir es explizit trotzdem wollen – optional)
            if (ConfigFile.Config.Project.Role.Kind != RoleKindEnum.Extension)
            {
                var outputsRootUsing = (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib || flatten)
                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Outputs"))
                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Outputs"));
                if (!root.Usings.Any(u => u.Name.ToString() == outputsRootUsing.Name.ToString()))
                    root = root.AddUsings(outputsRootUsing).NormalizeWhitespace();
            }

            if (HasCustomOutputs)
            {
                // Für Extension-Rollen immer DataContext.Outputs.<Schema> (keine Outputs.cs nötig)
                UsingDirectiveSyntax outputsSchemaUsing;
                if (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Extension)
                {
                    outputsSchemaUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Outputs.{schema.Name}"));
                }
                else
                {
                    outputsSchemaUsing = (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib || flatten)
                        ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Outputs.{schema.Name}"))
                        : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Outputs.{schema.Name}"));
                }
                if (!root.Usings.Any(u => u.Name.ToString() == outputsSchemaUsing.Name.ToString()))
                    root = root.AddUsings(outputsSchemaUsing).NormalizeWhitespace();
            }
        }
#pragma warning restore CS0618

        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Fallback: Some tests inject a minimal template without method placeholders.
        // The generation logic below expects at least two template method nodes to clone/transform.
        // If absent, we synthesize two minimal placeholder methods that follow the expected signature pattern:
        //   1) Base method (context, cancellationToken)
        //   2) Overload (this context, cancellationToken) – extension style
        // Parameter mutation logic later (InsertRange / RemoveAt) relies on having >=2 parameters.
        var existingMethodTemplates = classNode.Members.OfType<MethodDeclarationSyntax>().ToList();
        if (existingMethodTemplates.Count < 2)
        {
            // Ensure SqlParameter type is resolvable (add using if missing)
            if (!root.Usings.Any(u => u.Name.ToString() == "Microsoft.Data.SqlClient"))
            {
                root = root.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Microsoft.Data.SqlClient"))).NormalizeWhitespace();
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            }

            MethodDeclarationSyntax CreatePlaceholder(bool isExtension)
            {
                var ctxParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("context")).WithType(SyntaxFactory.ParseTypeName("IAppDbContext"));
                if (isExtension)
                {
                    ctxParam = ctxParam.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ThisKeyword)));
                }
                var ctParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("cancellationToken")).WithType(SyntaxFactory.ParseTypeName("CancellationToken"));
                var paramList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(new[] { ctxParam, ctParam }));
                // parameters placeholder (will be replaced for non-overload variant; kept simple here)
                var parametersDecl = SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                        .WithVariables(SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("parameters"))
                                .WithInitializer(SyntaxFactory.EqualsValueClause(
                                    SyntaxFactory.ObjectCreationExpression(
                                        SyntaxFactory.GenericName("List")
                                            .WithTypeArgumentList(
                                                SyntaxFactory.TypeArgumentList(
                                                    SyntaxFactory.SingletonSeparatedList<TypeSyntax>(SyntaxFactory.IdentifierName("SqlParameter")))))
                                        .WithInitializer(SyntaxFactory.InitializerExpression(SyntaxKind.CollectionInitializerExpression, SyntaxFactory.SeparatedList<ExpressionSyntax>())
                                ))))));

                // Concrete invocation shape for downstream string replacements:
                var invoke = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("context"),
                        SyntaxFactory.GenericName(SyntaxFactory.Identifier("ExecuteSingleAsync"))
                            .WithTypeArgumentList(
                                SyntaxFactory.TypeArgumentList(
                                    SyntaxFactory.SingletonSeparatedList<TypeSyntax>(SyntaxFactory.IdentifierName("CrudResult")))))
                    ).WithArgumentList(
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SeparatedList<ArgumentSyntax>(new SyntaxNodeOrToken[]
                            {
                                SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("schema.CrudAction"))),
                                SyntaxFactory.Token(SyntaxKind.CommaToken),
                                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("parameters")),
                                SyntaxFactory.Token(SyntaxKind.CommaToken),
                                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("cancellationToken"))
                            })));
                var returnStmt = SyntaxFactory.ReturnStatement(
                    SyntaxFactory.AwaitExpression(invoke));

                var method = SyntaxFactory.MethodDeclaration(
                        SyntaxFactory.ParseTypeName("Task<CrudResult>"),
                        SyntaxFactory.Identifier("CrudActionAsync"))
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword), SyntaxFactory.Token(SyntaxKind.AsyncKeyword)))
                    .WithParameterList(paramList)
                    .WithBody(SyntaxFactory.Block(parametersDecl, returnStmt));
                return method;
            }

            var placeholderBase = CreatePlaceholder(false);
            var placeholderOverload = CreatePlaceholder(true);
            classNode = classNode.AddMembers(placeholderBase, placeholderOverload);
            root = root.ReplaceNode((SyntaxNode)root.Members[0], nsNode.WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(new[] { classNode })));
        }

        // Generate Methods
        foreach (var storedProcedure in storedProcedures)
        {
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            // Base method (Raw or non-JSON typed behavior)
            var originMethodNode = (MethodDeclarationSyntax)classNode.Members.OfType<MethodDeclarationSyntax>().First();
            originMethodNode = GenerateStoredProcedureMethodText(originMethodNode, storedProcedure, StoredProcedureMethodKind.Raw, false);
            root = root.AddMethod(ref classNode, originMethodNode);

            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            // Overloaded extension with IAppDbContext
            var overloadOptionsMethodNode = (MethodDeclarationSyntax)classNode.Members.OfType<MethodDeclarationSyntax>().Skip(1).First();
            overloadOptionsMethodNode = GenerateStoredProcedureMethodText(overloadOptionsMethodNode, storedProcedure, StoredProcedureMethodKind.Raw, true);
            root = root.AddMethod(ref classNode, overloadOptionsMethodNode);

            // Add Deserialize variants for JSON returning procedures (inspect first result set)
            var firstSet = storedProcedure.ResultSets?.FirstOrDefault();
            if (firstSet?.ReturnsJson ?? false)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var methodTemplates = classNode.Members.OfType<MethodDeclarationSyntax>().ToList();
                var deserializePipeTemplate = methodTemplates[0];
                var deserializeContextTemplate = methodTemplates[1];

                var deserializePipe = GenerateStoredProcedureMethodText(deserializePipeTemplate, storedProcedure, StoredProcedureMethodKind.Deserialize, false);
                root = root.AddMethod(ref classNode, deserializePipe);

                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var deserializeContext = GenerateStoredProcedureMethodText(deserializeContextTemplate, storedProcedure, StoredProcedureMethodKind.Deserialize, true);
                root = root.AddMethod(ref classNode, deserializeContext);
            }
        }

        // Remove template Method
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        root = root.ReplaceNode(classNode, classNode.WithMembers([.. classNode.Members.Cast<MethodDeclarationSyntax>().Skip(2)]));

        // Ensure JSON deserialization namespace is present if any SP returns JSON
        if (storedProcedures.Any(sp => sp.ResultSets?.FirstOrDefault()?.ReturnsJson ?? false)
            && !root.Usings.Any(u => u.Name.ToString() == "System.Text.Json"))
        {
            root = root.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Text.Json"))).NormalizeWhitespace();
        }

        return TemplateManager.GenerateSourceText(root);
    }
#pragma warning restore CS0618

    private enum StoredProcedureMethodKind { Raw, Deserialize }

    private MethodDeclarationSyntax GenerateStoredProcedureMethodText(MethodDeclarationSyntax methodNode, Definition.StoredProcedure storedProcedure, StoredProcedureMethodKind kind, bool isOverload)
    {
        bool IsPseudoTabularCrud(Definition.StoredProcedure sp, StoredProcedureContentModel.ResultSet set)
        {
            if (sp == null || set == null) return false;
            var cols = set.Columns?.Count ?? 0;
            if (cols != 1) return false;
            var lone = set.Columns.First();
            bool loneIsNVarChar = (lone.SqlTypeName?.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase) ?? false);
            bool noRoot = !(set.JsonRootProperty?.Length > 0);
            // JsonPath removed – treat as flat if no nested JSON columns
            bool flatPath = (lone.IsNestedJson != true);
            bool legacyJsonSentinel = lone.Name.Equals("JSON_F52E2B61-18A1-11d1-B105-00805F49916B", StringComparison.OrdinalIgnoreCase);
            // Rein strukturelle Heuristik: Einzelne nvarchar(max) Spalte ohne Root/verschachtelten Pfad -> pseudo tabular
            return loneIsNVarChar && noRoot && flatPath && !legacyJsonSentinel;
        }
        // Method name
        var baseName = $"{storedProcedure.Name}Async";
        if (kind == StoredProcedureMethodKind.Deserialize)
        {
            var desired = $"{storedProcedure.Name}DeserializeAsync";
            // basic collision safeguard
            if (methodNode.Identifier.Text.Equals(desired, StringComparison.OrdinalIgnoreCase))
            {
                desired = $"{storedProcedure.Name}ToModelAsync";
            }
            baseName = desired;
        }
        var methodIdentifier = SyntaxFactory.ParseToken(baseName);
        methodNode = methodNode.WithIdentifier(methodIdentifier);

        var parameters = new[] { SyntaxFactory.Parameter(SyntaxFactory.Identifier("input"))
                                            .WithType(SyntaxFactory.ParseTypeName($"{storedProcedure.Name}Input")) };

        var parameterList = methodNode.ParameterList;
        parameterList = parameterList.WithParameters(
            parameterList.Parameters.InsertRange(2, parameters).RemoveAt(1)
        );
        var hasInputs = storedProcedure.HasInputs();
        if (!hasInputs)
        {
            parameterList = parameterList.WithParameters(
                parameterList.Parameters.RemoveAt(1)
            );
        }

        methodNode = methodNode.WithParameterList(parameterList);

        // Get Method Body as Statements
        var methodBody = methodNode.Body;
        var statements = methodBody.Statements.ToList();
        var returnExpression = (statements.Last() as ReturnStatementSyntax).Expression.GetText().ToString();

        if (isOverload)
        {
            returnExpression = returnExpression.Replace("CrudActionAsync", baseName);
            if (!hasInputs)
            {
                returnExpression = returnExpression.Replace("(input, ", "(");
            }
        }
        else
        {
            // Generate Sql-Parameters
            var sqlParamSyntax = (LocalDeclarationStatementSyntax)statements.Single(i => i is LocalDeclarationStatementSyntax);
            var sqlParamSyntaxIndex = statements.IndexOf(sqlParamSyntax);

            var arguments = new List<SyntaxNodeOrToken>();
            var inputs = storedProcedure.Input.ToList();
            var lastInput = inputs.LastOrDefault();
            inputs.ForEach(i =>
            {
                var isLastItem = i == lastInput;

                var args = new List<SyntaxNodeOrToken>
                {
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(i.Name[1..]))),
                    SyntaxFactory.Token(SyntaxKind.CommaToken),
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName($"input.{GetPropertyFromSqlInputTableType(i.Name)}"))
                };

                if (i.IsOutput)
                {
                    args.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
                    args.Add(SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
                }
                else if (i.MaxLength.HasValue)
                {
                    args.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
                    args.Add(SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)));
                }

                if (i.MaxLength.HasValue)
                {
                    args.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
                    args.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName($"{i.MaxLength}")));
                }

                arguments.Add(SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("AppDbContext"),
                            SyntaxFactory.IdentifierName(i.IsTableType ?? false ? "GetCollectionParameter" : "GetParameter")))
                        .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(args))));

                if (!isLastItem)
                {
                    arguments.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
                }
            });

            statements[sqlParamSyntaxIndex] = SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("parameters"))
                    .WithInitializer(SyntaxFactory.EqualsValueClause(
                        SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.GenericName(
                                SyntaxFactory.Identifier("List"))
                                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList<TypeSyntax>(SyntaxFactory.IdentifierName("SqlParameter")))))
                                    .WithInitializer(SyntaxFactory.InitializerExpression(SyntaxKind.CollectionInitializerExpression,
                                        SyntaxFactory.SeparatedList<ExpressionSyntax>(arguments))))))));

            methodBody = methodBody.WithStatements([.. statements.Skip(2)]);

            returnExpression = returnExpression.Replace("schema.CrudAction", storedProcedure.SqlObjectName);
        }

        methodNode = methodNode.WithBody(methodBody);

        // Replace ReturnType and ReturnLine
        var returnType = "Task<CrudResult>";
        var returnModel = "CrudResult";

        // Determine primary result set: prefer the first non-ExecSource placeholder, otherwise fall back to the first set.
        var firstSet = storedProcedure.ResultSets == null
                ? null
                : storedProcedure.ResultSets.FirstOrDefault(rs => string.IsNullOrEmpty(rs.ExecSourceProcedureName))
                    ?? storedProcedure.ResultSets.FirstOrDefault();
        var isJson = firstSet?.ReturnsJson ?? false;
        var isJsonArray = isJson && (firstSet?.ReturnsJsonArray ?? false);
        // Forwarding Referenz-only: genau ein Set, kein JSON, Columns leer, ExecSource gesetzt -> Ziel auflösen für Modellwahl
        bool isReferenceOnlyForward = false;
        string forwardSchema = null; string forwardProc = null;
        if (storedProcedure.ResultSets?.Count == 1 && firstSet != null && !isJson && (firstSet.Columns == null || firstSet.Columns.Count == 0)
            && !string.IsNullOrEmpty(firstSet.ExecSourceProcedureName))
        {
            isReferenceOnlyForward = true;
            forwardSchema = firstSet.ExecSourceSchemaName;
            forwardProc = firstSet.ExecSourceProcedureName;
        }
        if (isReferenceOnlyForward && !string.IsNullOrWhiteSpace(forwardSchema) && !string.IsNullOrWhiteSpace(forwardProc))
        {
            try
            {
                var schemasMeta = metadataProvider.GetSchemas();
                var targetSchema = schemasMeta.FirstOrDefault(s => s.Name.Equals(forwardSchema, StringComparison.OrdinalIgnoreCase));
                var targetSp = targetSchema?.StoredProcedures?.FirstOrDefault(sp => sp.Name.Equals(forwardProc, StringComparison.OrdinalIgnoreCase));
                if (targetSp != null)
                {
                    var rs0 = targetSp.Content?.ResultSets?.FirstOrDefault();
                    if (rs0 != null)
                    {
                        firstSet = rs0; // use real target structure for return type heuristic
                        isJson = rs0.ReturnsJson;
                        isJsonArray = isJson && rs0.ReturnsJsonArray;
                    }
                }
            }
            catch { /* best effort forward resolve */ }
        }
        // Heuristic: Some CRUD-like procedures are incorrectly classified as JSON while they only emit a single nvarchar(max) value (e.g. sub-select).
        // Downgrade criteria (structural only now): exactly 1 column, no explicit JsonRootProperty, column name not legacy FOR JSON sentinel,
        // column type starts with nvarchar, column.JsonPath equals column.Name (flat structure).
        if (isJson && firstSet != null)
        {
            // Structural-only downgrade (name-based CRUD heuristic removed) except when the parser explicitly flagged JSON array/without wrapper.
            // We now skip downgrade if the result set indicates array semantics or explicit JSON intent (ReturnsJsonArray true).
            var colCount = firstSet.Columns?.Count ?? 0;
            if (colCount == 1 && !firstSet.ReturnsJsonArray)
            {
                var col = firstSet.Columns[0];
                bool isNVarChar = (col.SqlTypeName?.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase) ?? false);
                bool isLegacyJsonSentinel = col.Name.Equals("JSON_F52E2B61-18A1-11d1-B105-00805F49916B", StringComparison.OrdinalIgnoreCase);
                bool hasRoot = !string.IsNullOrWhiteSpace(firstSet.JsonRootProperty);
                bool flatPath = col.IsNestedJson != true;
                if (isNVarChar && !isLegacyJsonSentinel && !hasRoot && flatPath)
                {
                    isJson = false;
                    isJsonArray = false;
                }
            }
        }

        // Only the pipe variant of a JSON Deserialize method performs the awaited deserialization; the context overload delegates.
        var requiresAsync = isJson && kind == StoredProcedureMethodKind.Deserialize && !isOverload;

        var rawJson = false;
        // Special case: multiple result sets but exactly one JSON set -> treat JSON as primary
        var totalSets = storedProcedure.ResultSets?.Count ?? 0;
        var jsonSetCount = storedProcedure.ResultSets?.Count(rs => rs.ReturnsJson) ?? 0;
        bool singleJsonAmongMultiple = totalSets > 1 && jsonSetCount == 1 && isJson;
        if ((isReferenceOnlyForward || singleJsonAmongMultiple) && isJson && kind == StoredProcedureMethodKind.Raw)
        {
            // Reference-only forwarding: raw method still returns a string pass-through, deserialize variant uses the forwarded target model.
            rawJson = true;
            returnType = "Task<string>";
            returnExpression = returnExpression
                .Replace("ExecuteSingleAsync<CrudResult>", "ReadJsonAsync")
                .Replace("ExecuteListAsync<CrudResult>", "ReadJsonAsync");
        }
        else if (isJson && kind == StoredProcedureMethodKind.Raw)
        {
            rawJson = true;
            // Raw JSON keeps Task<string> and we call ReadJsonAsync
            returnType = "Task<string>";
            returnExpression = returnExpression
                .Replace("ExecuteSingleAsync<CrudResult>", "ReadJsonAsync")
                .Replace("ExecuteListAsync<CrudResult>", "ReadJsonAsync");
        }
        else if (isJson && kind == StoredProcedureMethodKind.Deserialize)
        {
            returnModel = storedProcedure.Name;
            if (isJsonArray)
            {
                var hasOutputs = storedProcedure.HasOutputs();
                var wrapperType = hasOutputs ? $"JsonOutputOptions<List<{returnModel}>>" : $"List<{returnModel}>";
                returnType = hasOutputs ? $"Task<JsonOutputOptions<List<{returnModel}>>>" : $"Task<List<{returnModel}>>";
                if (isOverload)
                {
                    var call = $"context.CreatePipe().{storedProcedure.Name}DeserializeAsync({(storedProcedure.HasInputs() ? "input, " : string.Empty)}cancellationToken)";
                    returnExpression = call;
                }
                else
                {
                    var inner = $"await context.ReadJsonDeserializeAsync<List<{returnModel}>>(\"{storedProcedure.SqlObjectName}\", parameters, cancellationToken)";
                    if (hasOutputs)
                    {
                        // Build Output wrapper from parameters (expected OUTPUT params already present)
                        // Reuse ExecuteAsync signature logic? We construct Output manually via parameter extensions.
                        // Using parameters.ToOutput<Output>() to get base Output then wrap.
                        if (!methodNode.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)))
                        {
                            methodNode = methodNode.WithModifiers(methodNode.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword)));
                        }
                        inner = $"new JsonOutputOptions<List<{returnModel}>>(parameters.ToOutput<Output>(), {inner})";
                    }
                    returnExpression = inner;
                }
            }
            else
            {
                var hasOutputs = storedProcedure.HasOutputs();
                var wrapperType = hasOutputs ? $"JsonOutputOptions<{returnModel}>" : returnModel;
                returnType = hasOutputs ? $"Task<JsonOutputOptions<{returnModel}>>" : $"Task<{returnModel}>";
                if (isOverload)
                {
                    var call = $"context.CreatePipe().{storedProcedure.Name}DeserializeAsync({(storedProcedure.HasInputs() ? "input, " : string.Empty)}cancellationToken)";
                    returnExpression = call;
                }
                else
                {
                    var inner = $"await context.ReadJsonDeserializeAsync<{returnModel}>(\"{storedProcedure.SqlObjectName}\", parameters, cancellationToken)";
                    if (hasOutputs)
                    {
                        if (!methodNode.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)))
                        {
                            methodNode = methodNode.WithModifiers(methodNode.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword)));
                        }
                        inner = $"new JsonOutputOptions<{returnModel}>(parameters.ToOutput<Output>(), {inner})";
                    }
                    returnExpression = inner;
                }
            }
        }
        else if (!rawJson)
        {
            // Consolidated non-JSON, non-raw cases (scalar, output-based, tabular) for deterministic replacement
            string ReplacePlaceholder(string expr, string replacement)
            {
                // Only one placeholder should exist (ExecuteSingleAsync<CrudResult>). Future-proof: clean any stray list/single variants.
                return expr
                    .Replace("ExecuteListAsync<CrudResult>", replacement)
                    .Replace("ExecuteSingleAsync<CrudResult>", replacement)
                    .Replace("ExecuteAsync<CrudResult>", replacement);
            }

            if (storedProcedure.IsScalarResult())
            {
                var firstCol = firstSet?.Columns?.FirstOrDefault();
                if (firstCol != null && !string.IsNullOrWhiteSpace(firstCol.SqlTypeName))
                {
                    returnModel = ParseTypeFromSqlDbTypeName(firstCol.SqlTypeName, firstCol.IsNullable ?? true).ToString();
                }
                else
                {
                    returnModel = "string"; // conservative fallback
                }
                returnType = $"Task<{returnModel}>";
                returnExpression = ReplacePlaceholder(returnExpression, $"ExecuteScalarAsync<{returnModel}>");
            }
            else
            {
                var firstSet2 = firstSet; // use primary set for type heuristic
                var columnCount = firstSet2?.Columns?.Count ?? 0;
                var hasTabularResult = columnCount > 0;
                var hasOutputs = storedProcedure.HasOutputs();
                // Normalized skip list (without '@') for consistent detection
                var baseOutputPropSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ResultId", "RecordId", "RowVersion", "Result" };
                int customOutputCount = storedProcedure.GetOutputs()?.Count(o => !baseOutputPropSkip.Contains(o.Name.TrimStart('@'))) ?? 0;
                // Pseudo-tabular? Single nvarchar(max) column without root/complex structure -> treat as non-tabular (forces Output logic)
                bool pseudoTabularCrud = IsPseudoTabularCrud(storedProcedure, firstSet2);
                if (pseudoTabularCrud)
                {
                    hasTabularResult = false; // force Output logic
                }

                if (!hasTabularResult && hasOutputs && customOutputCount > 0)
                {
                    returnModel = storedProcedure.GetOutputTypeName();
                    returnType = $"Task<{returnModel}>";
                    returnExpression = ReplacePlaceholder(returnExpression, $"ExecuteAsync<{returnModel}>");
                }
                else if (!hasTabularResult && (!hasOutputs || customOutputCount == 0))
                {
                    returnModel = "Output";
                    returnType = "Task<Output>";
                    returnExpression = ReplacePlaceholder(returnExpression, "ExecuteAsync<Output>");
                }
                else
                {
                    var multiColumn = columnCount > 1;
                    returnModel = storedProcedure.Name;

                    // Legacy CRUD minimal result mapping (OBSOLETE - scheduled for removal):
                    // If the procedure name indicates Create/Update/Delete/Merge/Upsert AND there are NO custom outputs
                    // AND the first result set only contains [ResultId] and/or [RecordId] (no real data columns),
                    // we collapse to Output and map those columns into Output so consumers have a consistent pattern.
                    // This predates richer model generation and will be removed once callers are migrated.
                    var metaColsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "resultid", "recordid", "rowversion" };
                    var firstSetCols = firstSet2?.Columns?.Select(c => c.Name)?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();
                    bool onlyMetaColumns = firstSetCols.Count > 0 && firstSetCols.All(c => metaColsSet.Contains(c));
                    bool noCustomOutputs = !hasOutputs || customOutputCount == 0;

                    if (onlyMetaColumns && noCustomOutputs)
                    {
                        // Meta-only columns -> return CrudResult regardless of procedure name
                        returnType = "Task<CrudResult>";
                        returnExpression = ReplacePlaceholder(returnExpression, "ExecuteSingleAsync<CrudResult>");
                    }
                    else
                    {

                        // OBSOLETE heuristic (scheduled for removal): list vs single inference for *Find* / *List* names.
                        // Maintained temporarily for backward compatibility. Will be replaced by explicit metadata.
                        var nonOutputParams = storedProcedure.Input.Where(p => !p.IsOutput && !(p.IsTableType ?? false)).ToList();
                        bool IsIdName(string n) => n.Equals("@Id", StringComparison.OrdinalIgnoreCase) || n.EndsWith("Id", StringComparison.OrdinalIgnoreCase);
                        var idParams = nonOutputParams.Where(p => IsIdName(p.Name)).ToList();
                        bool singleIdParam = idParams.Count == 1 && nonOutputParams.Count == 1;
                        var nameLower = storedProcedure.Name.ToLowerInvariant();
                        bool nameSuggestsFind = nameLower.Contains("find") && !nameLower.Contains("list");
                        // Treat any FindBy* pattern as explicit single-row intent (not only FindById)
                        bool nameIsFindByPattern = nameLower.Contains("findby");
                        bool fewParams = nonOutputParams.Count <= 2;
                        // Force single row when:
                        //  - Explicit *FindById* pattern (even if multiple Id-like params exist, e.g. UserId + ClaimId + ComparisonCalculationId)
                        //  - Exactly one Id parameter and it is the only filter (legacy behaviour)
                        //  - Generic *Find* pattern with few params (<=2) and at least one Id param (conservative to avoid list-breaking)
                        bool forceSingle = nameIsFindByPattern || singleIdParam || (nameSuggestsFind && fewParams && idParams.Count >= 1);

                        var nameLower2All = storedProcedure.Name.ToLowerInvariant();
                        var indicatesListGlobal = nameLower2All.Contains("list");
                        if (indicatesListGlobal)
                        {
                            // Force list even if earlier single-row heuristics matched
                            forceSingle = false;
                        }
                        var indicatesFind = nameLower2All.Contains("find") && !nameLower2All.Contains("list");
                        // OBSOLETE naming heuristic (sunsetting once consumers migrate):
                        //   - Default: single (ExecuteSingleAsync)
                        //   - Only if procedure name contains "List" -> List<T>
                        //   - "FindBy*" & other forceSingle signals enforce single even with multiple columns
                        // Goal: explicit metadata will replace this. Do not add new special cases.
                        if (!forceSingle && indicatesListGlobal)
                        {
                            returnType = $"Task<List<{returnModel}>>";
                            returnExpression = ReplacePlaceholder(returnExpression, $"ExecuteListAsync<{returnModel}>");
                        }
                        else
                        {
                            returnType = $"Task<{returnModel}>";
                            returnExpression = ReplacePlaceholder(returnExpression, $"ExecuteSingleAsync<{returnModel}>");
                        }
                    }
                }
            }
        }

        methodNode = methodNode.WithReturnType(SyntaxFactory.ParseTypeName(returnType).WithTrailingTrivia(SyntaxFactory.Space));

        if (requiresAsync && !methodNode.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)))
        {
            methodNode = methodNode.WithModifiers(methodNode.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword)));
        }

        var returnStatementSyntax = statements.Single(i => i is ReturnStatementSyntax);
        var returnStatementSyntaxIndex = statements.IndexOf(returnStatementSyntax);

        statements[returnStatementSyntaxIndex] = SyntaxFactory.ReturnStatement(SyntaxFactory.ParseExpression(returnExpression).WithLeadingTrivia(SyntaxFactory.Space))
            .WithLeadingTrivia(SyntaxFactory.Tab, SyntaxFactory.Tab, SyntaxFactory.Tab)
            .WithTrailingTrivia(SyntaxFactory.CarriageReturn);

        methodBody = methodBody.WithStatements([.. statements]);
        methodNode = methodNode.WithBody(methodBody);

        // Add XML documentation for JSON methods
        if (isJson)
        {
            var xmlSummary = string.Empty;
            if (kind == StoredProcedureMethodKind.Raw)
            {
                xmlSummary =
                    $"/// <summary>Executes stored procedure '{storedProcedure.SqlObjectName}' and returns the raw JSON string.</summary>\r\n" +
                    $"/// <remarks>Use <see cref=\"{storedProcedure.Name}DeserializeAsync\"/> to obtain a typed {(isJsonArray ? "list" : "model")}.</remarks>\r\n";
            }
            else if (kind == StoredProcedureMethodKind.Deserialize)
            {
                var target = isJsonArray ? $"List<{storedProcedure.Name}>" : storedProcedure.Name;
                xmlSummary =
                    $"/// <summary>Executes stored procedure '{storedProcedure.SqlObjectName}' and deserializes the JSON response into {target}.</summary>\r\n" +
                    $"/// <remarks>Underlying raw JSON method: <see cref=\"{storedProcedure.Name}Async\"/>.</remarks>\r\n";
            }

            if (!string.IsNullOrWhiteSpace(xmlSummary))
            {
                // Prepend documentation, preserving existing leading trivia
                var leading = methodNode.GetLeadingTrivia();
                var docTrivia = SyntaxFactory.ParseLeadingTrivia(xmlSummary);
                methodNode = methodNode.WithLeadingTrivia(docTrivia.AddRange(leading));
            }
        }

        return methodNode.NormalizeWhitespace();
    }

    public async Task GenerateDataContextStoredProceduresAsync(bool isDryRun)
    {
        try
        {
            var allSchemasDiag = metadataProvider.GetSchemas();
            var buildSchemasDiag = allSchemasDiag.Where(s => s.Status == SchemaStatusEnum.Build).ToList();
            var totalSp = buildSchemasDiag.Sum(s => (s.StoredProcedures?.Count() ?? 0));
            consoleService.Verbose($"[diag-sp] schemas(build)={buildSchemasDiag.Count} procedures={totalSp} dryRun={isDryRun}");
        }
        catch { /* ignore diag */ }
        var schemas = metadataProvider.GetSchemas()
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema)
            .ToList();

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures.ToList();

            if (!storedProcedures.Any())
            {
                continue;
            }

            var dataContextStoredProcedurePath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.StoredProcedures.Path);
            var path = Path.Combine(dataContextStoredProcedurePath, schema.Path);
            consoleService.Verbose($"[diag-sp] targetDir={path} schema={schema.Name}");
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var groupedStoredProcedures in storedProcedures.GroupBy(i => i.Name, (key, group) => group.ToList()))
            {
                var entityName = groupedStoredProcedures.First().Name;

                var fileName = $"{entityName}Extensions.cs";
                var fileNameWithPath = Path.Combine(path, fileName);

                var sourceText = await GetStoredProcedureExtensionsCodeAsync(schema, groupedStoredProcedures);

                await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);

                // Verbose trace to help diagnose empty StoredProcedures directory issues
                try
                {
                    consoleService.Verbose($"[sp-generator] wrote {fileName} (procedures={groupedStoredProcedures.Count}) to {path}");
                }
                catch { /* ignore logging errors */ }
            }

            // Safety: if loop wrote nothing despite storedProcedures.Any(), log anomaly
            if (!Directory.EnumerateFiles(path, "*Extensions.cs").Any())
            {
                var warnMsg = $"[sp-generator][warn] No extension files generated for schema '{schema.Name}' though {storedProcedures.Count} procedures present (check filters & statuses)";
                consoleService.Verbose(warnMsg);
            }
        }
    }
}

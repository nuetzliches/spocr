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
            var set = sp.ResultSets?.FirstOrDefault();
            if (set == null) return false; // zero-result => no model
            var returnsJson = set.ReturnsJson;
            var hasCols = set.Columns?.Any() ?? false;
            var scalarNonJson = hasCols && !returnsJson && set.Columns.Count == 1; // skipped by model generator
            if (!returnsJson && scalarNonJson) return false;
            return true; // multi-col, json, or other tabular
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
        var baseOutputSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "@ResultId", "@RecordId", "@RowVersion", "@Result" };
        bool HasAnyOutputs = storedProcedures.Any(sp => sp.GetOutputs()?.Any() ?? false);
        bool HasCustomOutputs = storedProcedures.Any(sp => (sp.GetOutputs()?.Count(o => !baseOutputSkip.Contains(o.Name)) ?? 0) > 0);
        // For Extension role we do not generate local Outputs namespace; these come from Lib.
        // Skip adding DataContext Outputs usings entirely when role is Extension.
#pragma warning disable CS0618
        if (HasAnyOutputs && ConfigFile.Config.Project.Role.Kind != RoleKindEnum.Extension)
#pragma warning restore CS0618
        {
#pragma warning disable CS0618
            var flatten = string.IsNullOrWhiteSpace(ConfigFile.Config.Project.Output?.DataContext?.Path) || ConfigFile.Config.Project.Output.DataContext.Path.TrimEnd('/', '\\') == ".";
            var outputsRootUsing = (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib || flatten)
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Outputs"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Outputs"));
            if (!root.Usings.Any(u => u.Name.ToString() == outputsRootUsing.Name.ToString()))
                root = root.AddUsings(outputsRootUsing).NormalizeWhitespace();

            if (HasCustomOutputs)
            {
                var outputsSchemaUsing = (ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib || flatten)
                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Outputs.{schema.Name}"))
                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Outputs.{schema.Name}"));
                if (!root.Usings.Any(u => u.Name.ToString() == outputsSchemaUsing.Name.ToString()))
                    root = root.AddUsings(outputsSchemaUsing).NormalizeWhitespace();
            }
#pragma warning restore CS0618
        }

        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Generate Methods
        foreach (var storedProcedure in storedProcedures)
        {
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            // Base method (Raw or non-JSON typed behavior)
            var originMethodNode = (MethodDeclarationSyntax)classNode.Members[0];
            originMethodNode = GenerateStoredProcedureMethodText(originMethodNode, storedProcedure, StoredProcedureMethodKind.Raw, false);
            root = root.AddMethod(ref classNode, originMethodNode);

            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            // Overloaded extension with IAppDbContext
            var overloadOptionsMethodNode = (MethodDeclarationSyntax)classNode.Members[1];
            overloadOptionsMethodNode = GenerateStoredProcedureMethodText(overloadOptionsMethodNode, storedProcedure, StoredProcedureMethodKind.Raw, true);
            root = root.AddMethod(ref classNode, overloadOptionsMethodNode);

            // Add Deserialize variants for JSON returning procedures (inspect first result set)
            var firstSet = storedProcedure.ResultSets?.FirstOrDefault();
            if (firstSet?.ReturnsJson ?? false)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var deserializePipeTemplate = (MethodDeclarationSyntax)classNode.Members[0];
                var deserializeContextTemplate = (MethodDeclarationSyntax)classNode.Members[1];

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

        var firstSet = storedProcedure.ResultSets?.FirstOrDefault();
        var isJson = firstSet?.ReturnsJson ?? false;
        var isJsonArray = isJson && (firstSet?.ReturnsJsonArray ?? false);

        // Only the pipe variant of a JSON Deserialize method performs the awaited deserialization; the context overload delegates.
        var requiresAsync = isJson && kind == StoredProcedureMethodKind.Deserialize && !isOverload;

        var rawJson = false;
        if (isJson && kind == StoredProcedureMethodKind.Raw)
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
                var firstSet2 = storedProcedure.ResultSets?.FirstOrDefault();
                var columnCount = firstSet2?.Columns?.Count ?? 0;
                var hasTabularResult = columnCount > 0;
                var hasOutputs = storedProcedure.HasOutputs();
                var baseOutputPropSkip = new[] { "@ResultId", "@RecordId", "@RowVersion", "@Result" };
                var customOutputCount = storedProcedure.GetOutputs()?.Count(o => !baseOutputPropSkip.Contains(o.Name, StringComparer.OrdinalIgnoreCase)) ?? 0;

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
                    var nameLowerCrud = storedProcedure.Name.ToLowerInvariant();
                    bool isCrudVerb = nameLowerCrud.Contains("create") || nameLowerCrud.Contains("update") || nameLowerCrud.Contains("delete") || nameLowerCrud.Contains("merge") || nameLowerCrud.Contains("upsert");
                    // Special cases: procedures without a classic CRUD verb that still only emit meta columns
                    // and should be treated as minimal CRUD (fallback -> CrudResult)
                    var crudVerbWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "invoicesend"
                    };
                    if (crudVerbWhitelist.Contains(nameLowerCrud))
                    {
                        isCrudVerb = true;
                    }
                    var crudAllowedCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "resultid", "recordid" };
                    var firstSetCols = firstSet2?.Columns?.Select(c => c.Name)?.ToList() ?? new List<string>();
                    bool onlyCrudMetaColumns = firstSetCols.Count > 0 && firstSetCols.All(c => crudAllowedCols.Contains(c));
                    bool noCustomOutputs = !hasOutputs || customOutputCount == 0;

                    if (isCrudVerb && onlyCrudMetaColumns && noCustomOutputs)
                    {
                        // OBSOLETE CRUD minimal result heuristic: collapse meta-only resultset to CrudResult
                        returnType = "Task<CrudResult>";
                        returnExpression = ReplacePlaceholder(returnExpression, $"ExecuteSingleAsync<CrudResult>");
                        // Skip obsolete list/find heuristic for this branch
                    }
                    else
                    {

                        // OBSOLETE Heuristic (scheduled for removal): List vs Single inference for *Find* / *List* names.
                        // Maintained temporarily for backward compatibility. Will be replaced by explicit metadata.
                        var nonOutputParams = storedProcedure.Input.Where(p => !p.IsOutput && !(p.IsTableType ?? false)).ToList();
                        bool IsIdName(string n) => n.Equals("@Id", StringComparison.OrdinalIgnoreCase) || n.EndsWith("Id", StringComparison.OrdinalIgnoreCase);
                        var idParams = nonOutputParams.Where(p => IsIdName(p.Name)).ToList();
                        bool singleIdParam = idParams.Count == 1 && nonOutputParams.Count == 1;
                        var nameLower = storedProcedure.Name.ToLowerInvariant();
                        bool nameSuggestsFind = nameLower.Contains("find") && !nameLower.Contains("list");
                        // Treat any pattern FindBy* as explicit single-row intent (not nur FindById)
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
        var schemas = metadataProvider.GetSchemas()
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures.ToList();

            if (!storedProcedures.Any())
            {
                continue;
            }

            var dataContextStoredProcedurePath = DirectoryUtils.GetWorkingDirectory(ConfigFile.Config.Project.Output.DataContext.Path, ConfigFile.Config.Project.Output.DataContext.StoredProcedures.Path);
            var path = Path.Combine(dataContextStoredProcedurePath, schema.Path);
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

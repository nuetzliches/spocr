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
    public async Task<SourceText> GetStoredProcedureExtensionsCodeAsync(Definition.Schema schema, List<Definition.StoredProcedure> storedProcedures)
    {
        var entityName = storedProcedures.First().EntityName;

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
            // For libs and default projects
            // Add Using for common Models (e.g. CrudResult)
            if (storedProcedures.Any(i => i.ReadWriteKind == Definition.ReadWriteKindEnum.Write))
            {
                for (var i = 0; i < root.Usings.Count; i++)
                {
                    var usingDirective = root.Usings[i];
                    var newUsingName = ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib
                        ? SyntaxFactory.ParseName($"{usingDirective.Name.ToString().Replace("Source.DataContext", ConfigFile.Config.Project.Output.Namespace)}")
                        : SyntaxFactory.ParseName($"{usingDirective.Name.ToString().Replace("Source", ConfigFile.Config.Project.Output.Namespace)}");
                    root = root.ReplaceNode(usingDirective, usingDirective.WithName(newUsingName));
                }
            }
        }

        // Add Using for Models (non-JSON, multi-column READ procedures)
        if (storedProcedures.Any(sp => sp.ReadWriteKind == Definition.ReadWriteKindEnum.Read
                           && !(sp.ResultSets?.FirstOrDefault()?.ReturnsJson ?? false)
                           && ((sp.ResultSets?.FirstOrDefault()?.Columns?.Count) ?? 0) > 1))
        {
            var modelUsingDirective = ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Models.{schema.Name}"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Models.{schema.Name}"));
            root = root.AddUsings(modelUsingDirective).NormalizeWhitespace();
        }

        // Add Usings for Inputs
        if (storedProcedures.Any(s => s.HasInputs()))
        {
            var inputUsingDirective = ConfigFile.Config.Project.Role.Kind == RoleKindEnum.Lib
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

        // Remove Template Usings
        var usings = root.Usings.Where(_ => !_.Name.ToString().StartsWith("Source."));
        root = root.WithUsings([.. usings]);

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
                returnType = $"Task<List<{returnModel}>>";
                // Call raw method (which returns JSON string) then deserialize to list; fallback to empty list if null
                var rawCall = $"{storedProcedure.Name}Async({(storedProcedure.HasInputs() ? "input, " : string.Empty)}cancellationToken)";
                returnExpression = $"System.Text.Json.JsonSerializer.Deserialize<List<{returnModel}>>(await {rawCall}, new System.Text.Json.JsonSerializerOptions {{ PropertyNameCaseInsensitive = true }}) ?? new List<{returnModel}>()";
            }
            else
            {
                returnType = $"Task<{returnModel}>";
                var rawCall = $"{storedProcedure.Name}Async({(storedProcedure.HasInputs() ? "input, " : string.Empty)}cancellationToken)";
                returnExpression = $"System.Text.Json.JsonSerializer.Deserialize<{returnModel}>(await {rawCall}, new System.Text.Json.JsonSerializerOptions {{ PropertyNameCaseInsensitive = true }})";
            }
        }
        else if (!rawJson && storedProcedure.IsScalarResult())
        {
            // Scalar non-JSON: derive type from first column metadata if available
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
            returnExpression = returnExpression.Replace("ExecuteSingleAsync<CrudResult>", $"ExecuteScalarAsync<{returnModel}>");
        }
        else if (!rawJson)
        {
            switch (storedProcedure.OperationKind)
            {
                case Definition.OperationKindEnum.Find:
                case Definition.OperationKindEnum.List:
                    returnModel = storedProcedure.Name;
                    break;
            }

            switch (storedProcedure.ResultKind)
            {
                case Definition.ResultKindEnum.Single:
                    returnType = $"Task<{returnModel}>";
                    returnExpression = returnExpression.Replace("ExecuteSingleAsync<CrudResult>", $"ExecuteSingleAsync<{returnModel}>");
                    break;
                case Definition.ResultKindEnum.List:
                    returnType = $"Task<List<{returnModel}>>";
                    returnExpression = returnExpression.Replace("ExecuteSingleAsync<CrudResult>", $"ExecuteListAsync<{returnModel}>");
                    break;
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
            var storedProcedures = schema.StoredProcedures;

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

            foreach (var groupedStoredProcedures in storedProcedures.GroupBy(i => i.EntityName, (key, group) => group.ToList()))
            {
                var entityName = groupedStoredProcedures.First().EntityName;

                var fileName = $"{entityName}Extensions.cs";
                var fileNameWithPath = Path.Combine(path, fileName);

                var sourceText = await GetStoredProcedureExtensionsCodeAsync(schema, groupedStoredProcedures);

                await Output.WriteAsync(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

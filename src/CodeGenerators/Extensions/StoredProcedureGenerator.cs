using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpocR.CodeGenerators.Base;
using SpocR.Contracts;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Extensions;

public class StoredProcedureGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IReportService reportService
) : GeneratorBase(configFile, output, reportService)
{
    public SourceText GetStoredProcedureText(Definition.Schema schema, List<Definition.StoredProcedure> storedProcedures)
    {
        var entityName = storedProcedures.First().EntityName;

        var rootDir = Output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "StoredProcedures", "StoredProcedureExtensions.cs"));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // If its an extension, add usings for the lib
        if (ConfigFile.Config.Project.Role.Kind == ERoleKind.Extension)
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
                    var newUsingName = ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib
                        ? SyntaxFactory.ParseName($"{usingDirective.Name.ToString().Replace("Source.DataContext", ConfigFile.Config.Project.Output.Namespace)}")
                        : SyntaxFactory.ParseName($"{usingDirective.Name.ToString().Replace("Source", ConfigFile.Config.Project.Output.Namespace)}");
                    root = root.ReplaceNode(usingDirective, usingDirective.WithName(newUsingName));
                }
            }
        }

        // Add Using for Models
        if (storedProcedures.Any(i => i.ReadWriteKind == Definition.ReadWriteKindEnum.Read && i.Output?.Count() > 1))
        {
            var modelUsingDirective = ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Models.{schema.Name}"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Models.{schema.Name}"));
            root = root.AddUsings(modelUsingDirective).NormalizeWhitespace();
        }

        // Add Usings for Inputs
        if (storedProcedures.Any(s => s.HasInputs()))
        {
            var inputUsingDirective = ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Inputs.{schema.Name}"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Inputs.{schema.Name}"));
            root = root.AddUsings(inputUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        // Add Usings for Outputs
        if (storedProcedures.Any(s => s.HasOutputs() && !s.IsDefaultOutput()))
        {
            var inputUsingDirective = ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.Outputs.{schema.Name}"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.Outputs.{schema.Name}"));
            root = root.AddUsings(inputUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        // Add Usings for TableTypes
        // If Inputs contains a TableType, add using for TableTypes
        var tableTypeSchemas = storedProcedures.SelectMany(sp => sp.Input.Where(i => i.IsTableType ?? false))
                             .GroupBy(t => t.TableTypeSchemaName, (key, group) => key).ToList();

        foreach (var tableTypeSchema in tableTypeSchemas)
        {
            var tableTypeSchemaConfig = ConfigFile.Config.Schema.Find(s => s.Name.Equals(tableTypeSchema));
            var useFromLib = tableTypeSchemaConfig?.Status != SchemaStatusEnum.Build
                && ConfigFile.Config.Project.Role.Kind == ERoleKind.Extension;

            var paramUsingDirective = useFromLib
                                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Role.LibNamespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                                : ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib
                                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ConfigFile.Config.Project.Output.Namespace}.DataContext.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
            root = root.AddUsings(paramUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        // Remove Template Usings
        var usings = root.Usings.Where(_ => !_.Name.ToString().StartsWith("Source."));
        root = root.WithUsings([.. usings]);

        // Replace Namespace
        if (ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", ConfigFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", ConfigFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }

        // Replace ClassName
        root = root.ReplaceClassName(ci => ci.Replace("StoredProcedure", entityName));

        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Generate Methods
        foreach (var storedProcedure in storedProcedures)
        {
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            // Extension for IAppDbContextPipe
            var originMethodNode = (MethodDeclarationSyntax)classNode.Members[0];
            originMethodNode = GenerateStoredProcedureMethodText(originMethodNode, storedProcedure);
            root = root.AddMethod(ref classNode, originMethodNode);

            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            // Overloaded extension with IAppDbContext
            var overloadOptionsMethodNode = (MethodDeclarationSyntax)classNode.Members[1];
            overloadOptionsMethodNode = GenerateStoredProcedureMethodText(overloadOptionsMethodNode, storedProcedure, true);
            root = root.AddMethod(ref classNode, overloadOptionsMethodNode);
        }

        // Remove template Method
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        root = root.ReplaceNode(classNode, classNode.WithMembers([.. classNode.Members.Cast<MethodDeclarationSyntax>().Skip(2)]));

        return root.NormalizeWhitespace().GetText();
    }

    private MethodDeclarationSyntax GenerateStoredProcedureMethodText(MethodDeclarationSyntax methodNode, Definition.StoredProcedure storedProcedure, bool isOverload = false)
    {
        // Replace MethodName
        var methodName = $"{storedProcedure.Name}Async";
        var methodIdentifier = SyntaxFactory.ParseToken(methodName);
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
            returnExpression = returnExpression.Replace("CrudActionAsync", methodName);
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
                            SyntaxFactory.IdentifierName((i.IsTableType ?? false) ? "GetCollectionParameter" : "GetParameter")))
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

        if (!storedProcedure.HasResult() && storedProcedure.HasOutputs())
        {
            var outputType = storedProcedure.GetOutputTypeName();

            returnType = $"Task<{outputType}>";
            returnExpression = returnExpression.Replace("ExecuteSingleAsync<CrudResult>", $"ExecuteAsync<{outputType}>");
        }
        else if (storedProcedure.IsScalarResult())
        {
            var output = storedProcedure.Output.FirstOrDefault();
            returnModel = ParseTypeFromSqlDbTypeName(output.SqlTypeName, output.IsNullable ?? false).ToString();

            returnType = $"Task<{returnModel}>";
            returnExpression = returnExpression.Replace("ExecuteSingleAsync<CrudResult>", $"ReadJsonAsync");
        }
        else
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

        var returnStatementSyntax = statements.Single(i => i is ReturnStatementSyntax);
        var returnStatementSyntaxIndex = statements.IndexOf(returnStatementSyntax);

        statements[returnStatementSyntaxIndex] = SyntaxFactory.ReturnStatement(SyntaxFactory.ParseExpression(returnExpression).WithLeadingTrivia(SyntaxFactory.Space))
            .WithLeadingTrivia(SyntaxFactory.Tab, SyntaxFactory.Tab, SyntaxFactory.Tab)
            .WithTrailingTrivia(SyntaxFactory.CarriageReturn);

        methodBody = methodBody.WithStatements([.. statements]);
        methodNode = methodNode.WithBody(methodBody);

        return methodNode.NormalizeWhitespace();
    }

    public void GenerateDataContextStoredProcedures(bool isDryRun)
    {
        var schemas = ConfigFile.Config.Schema
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

                var sourceText = GetStoredProcedureText(schema, groupedStoredProcedures);

                Output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}

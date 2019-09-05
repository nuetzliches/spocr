using System;
using System.Collections.Generic;
using System.Data;
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

namespace SpocR
{
    public class Generator
    {
        private readonly FileManager<ConfigurationModel> _configFile;
        private readonly SpocrService _spocr;
        private readonly OutputService _output;

        public Generator(FileManager<ConfigurationModel> configFile, SpocrService spocr, OutputService output)
        {
            _configFile = configFile;
            _spocr = spocr;
            _output = output;
        }

        public TypeSyntax ParseTypeFromSqlDbTypeName(string sqlTypeName, bool isNullable)
        {
            sqlTypeName = sqlTypeName.Split('(')[0];
            var sqlType = (SqlDbType)Enum.Parse(typeof(SqlDbType), sqlTypeName, true);
            var clrType = SqlDbHelper.GetType(sqlType, isNullable);
            return SyntaxFactory.ParseTypeName(clrType.ToGenericTypeString());
        }

        public string GetInputTypeNameForTableType(Definition.StoredProcedure storedProcedure, StoredProcedureInputModel input)
        {
            return $"{storedProcedure.Name}{input.Name.Replace("@", "")}";
        }

        public TypeSyntax GetInputTypeForTableType(Definition.StoredProcedure storedProcedure, StoredProcedureInputModel input)
        {
            var typeName = $"IEnumerable<{GetInputTypeNameForTableType(storedProcedure, input)}>";
            return SyntaxFactory.ParseTypeName(typeName);
        }

        public SourceText GetModelTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
        {
            var rootDir = _output.GetOutputRootDir();
            var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "Models", "Model.cs"));

            var tree = CSharpSyntaxTree.ParseText(fileContent);
            var root = tree.GetCompilationUnitRoot();

            // Replace Namespace
            if (_configFile.Config.Project.Role.Kind == ERoleKind.Lib)
            {
                root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", _configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
            }
            else
            {
                root = root.ReplaceNamespace(ns => ns.Replace("Source", _configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
            }

            // Replace ClassName
            root = root.ReplaceClassName(ci => ci.Replace("Model", storedProcedure.Name));

            // Generate Properies
            // https://stackoverflow.com/questions/45160694/adding-new-field-declaration-to-class-with-roslyn
            var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];
            foreach (var item in storedProcedure.Output)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];

                var propertyIdentifier = SyntaxFactory.ParseToken($" {item.Name} ");
                propertyNode = propertyNode
                    .WithType(ParseTypeFromSqlDbTypeName(item.SqlTypeName, item.IsNullable));

                propertyNode = propertyNode
                    .WithIdentifier(propertyIdentifier);

                root = root.AddProperty(classNode, propertyNode);
            }

            // Remove template Property
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            root = root.ReplaceNode(classNode, classNode.WithMembers(new SyntaxList<MemberDeclarationSyntax>(classNode.Members.Cast<PropertyDeclarationSyntax>().Skip(1))));

            return root.GetText();
        }

        public SourceText GetParamsTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
        {
            var rootDir = _output.GetOutputRootDir();
            var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "Params", "Params.cs"));

            var tree = CSharpSyntaxTree.ParseText(fileContent);
            var root = tree.GetCompilationUnitRoot();

            // Replace Namespace
            if (_configFile.Config.Project.Role.Kind == ERoleKind.Lib)
            {
                root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", _configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
            }
            else
            {
                root = root.ReplaceNamespace(ns => ns.Replace("Source", _configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
            }

            // If its an extension, add usings for the lib
            if (_configFile.Config.Project.Role.Kind == ERoleKind.Extension)
            {
                var libModelUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Role.LibNamespace}.Params"));
                root = root.AddUsings(libModelUsingDirective).NormalizeWhitespace();
            }

            var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var inputs = storedProcedure.Input.Where(i => i.IsTableType ?? false);
            var classNodeIx = 0;
            foreach (var input in inputs)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];

                // Replace ClassName
                var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var classIdentifier = SyntaxFactory.ParseToken($"{classNode.Identifier.ValueText.Replace("Params", $"{GetInputTypeNameForTableType(storedProcedure, input)}")} ");
                classNode = classNode.WithIdentifier(classIdentifier);

                root = root.ReplaceNode(nsNode, nsNode.AddMembers(classNode));
                classNodeIx++;

                // Create Properties
                if (input.Columns != null)
                {
                    foreach (var column in input.Columns)
                    {
                        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                        classNode = (ClassDeclarationSyntax)nsNode.Members[classNodeIx];
                        var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];

                        var propertyIdentifier = SyntaxFactory.ParseToken($" {column.Name} ");

                        propertyNode = propertyNode
                            .WithType(ParseTypeFromSqlDbTypeName(column.SqlTypeName, column.IsNullable ?? false));

                        propertyNode = propertyNode
                            .WithIdentifier(propertyIdentifier);

                        root = root.AddProperty(classNode, propertyNode);
                    }
                }

                // Remove template Property
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[classNodeIx];
                root = root.ReplaceNode(classNode, classNode.WithMembers(new SyntaxList<MemberDeclarationSyntax>(classNode.Members.Cast<PropertyDeclarationSyntax>().Skip(1))));

            }

            // Remove template Class
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            root = root.ReplaceNode(nsNode, nsNode.WithMembers(new SyntaxList<MemberDeclarationSyntax>(nsNode.Members.Cast<ClassDeclarationSyntax>().Skip(1))));

            return root.GetText();
        }

        public void GenerateDataContextParams(bool dryrun)
        {
            var schemas = _configFile.Config.Schema
                .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
                .Select(i => Definition.ForSchema(i));

            foreach (var schema in schemas)
            {
                var storedProcedures = schema.StoredProcedures
                    .Where(sp => sp.Input.Any(i => (i.IsTableType ?? false)));

                if (!(storedProcedures.Any()))
                {
                    continue;
                }

                var dataContextParamsPath = DirectoryUtils.GetWorkingDirectory(_configFile.Config.Project.Output.DataContext.Path, _configFile.Config.Project.Output.DataContext.Params.Path);
                var path = Path.Combine(dataContextParamsPath, schema.Path);
                if (!Directory.Exists(path) && !dryrun)
                {
                    Directory.CreateDirectory(path);
                }

                foreach (var storedProcedure in storedProcedures)
                {
                    var fileName = Path.Combine(path, $"{storedProcedure.Name}Params.cs");
                    var sourceText = GetParamsTextForStoredProcedure(schema, storedProcedure);

                    if (ExistingFileMatches(fileName, sourceText))
                    {
                        // Existing Params and new Params matches
                        continue;
                    }

                    if (!dryrun)
                        File.WriteAllText(fileName, sourceText.WithMetadataToString(_spocr.Version));
                }
            }
        }

        public void GenerateDataContextModels(bool dryrun)
        {
            var schemas = _configFile.Config.Schema
                .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
                .Select(i => Definition.ForSchema(i));

            foreach (var schema in schemas)
            {
                var storedProcedures = schema.StoredProcedures
                    .Where(i => i.ReadWriteKind == Definition.ReadWriteKindEnum.Read);

                if (!(storedProcedures.Any()))
                {
                    continue;
                }

                var dataContextModelPath = DirectoryUtils.GetWorkingDirectory(_configFile.Config.Project.Output.DataContext.Path, _configFile.Config.Project.Output.DataContext.Models.Path);
                var path = Path.Combine(dataContextModelPath, schema.Path);
                if (!Directory.Exists(path) && !dryrun)
                {
                    Directory.CreateDirectory(path);
                }

                foreach (var storedProcedure in storedProcedures)
                {
                    var isScalar = storedProcedure.Output.Count() == 1;
                    if (isScalar)
                    {
                        continue;
                    }

                    var fileName = Path.Combine(path, $"{storedProcedure.Name}.cs");
                    var sourceText = GetModelTextForStoredProcedure(schema, storedProcedure);

                    if (ExistingFileMatches(fileName, sourceText))
                    {
                        // Existing Model and new Model matches
                        continue;
                    }

                    if (!dryrun)
                        File.WriteAllText(fileName, sourceText.WithMetadataToString(_spocr.Version));
                }
            }
        }

        private bool ExistingFileMatches(string fileName, SourceText sourceText)
        {
            if (File.Exists(fileName))
            {
                var oldFileContent = File.ReadAllText(fileName);
                var oldTree = CSharpSyntaxTree.ParseText(oldFileContent);
                var oldRoot = oldTree.GetCompilationUnitRoot();
                var oldNsNode = (NamespaceDeclarationSyntax)oldRoot.Members[0];

                var newTree = CSharpSyntaxTree.ParseText(sourceText);
                var newRoot = newTree.GetCompilationUnitRoot();
                var newNsNode = (NamespaceDeclarationSyntax)newRoot.Members[0];

                if (oldNsNode.GetText().ToString().Equals(newNsNode.GetText().ToString()))
                {
                    return true;
                }
            }
            return false;
        }

        public string GetIdentifierFromSqlInputParam(string name)
        {
            name = $"{name.Remove(0, 1).FirstCharToLower()}";
            var reservedKeyWords = new[] { "params" };
            if (reservedKeyWords.Contains(name))
            {
                name = $"{name}_";
            }
            return name;
        }

        public SourceText GetStoredProcedureText(Definition.Schema schema, List<Definition.StoredProcedure> storedProcedures)
        {
            var first = storedProcedures.First();
            var rootDir = _output.GetOutputRootDir();
            var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "StoredProcedures", "StoredProcedureExtensions.cs"));

            var tree = CSharpSyntaxTree.ParseText(fileContent);
            var root = tree.GetCompilationUnitRoot();

            // Replace Usings
            for (var i = 0; i < root.Usings.Count; i++)
            {
                var usingDirective = root.Usings[i];
                var newUsingName = _configFile.Config.Project.Role.Kind == ERoleKind.Lib
                    ? SyntaxFactory.ParseName($"{usingDirective.Name.ToString().Replace("Source.DataContext", _configFile.Config.Project.Output.Namespace)}")
                    : SyntaxFactory.ParseName($"{usingDirective.Name.ToString().Replace("Source", _configFile.Config.Project.Output.Namespace)}");
                root = root.ReplaceNode(usingDirective, usingDirective.WithName(newUsingName));
            }

            // If its an extension, add usings for the lib
            if (_configFile.Config.Project.Role.Kind == ERoleKind.Extension)
            {
                var libUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Role.LibNamespace}"));
                root = root.AddUsings(libUsingDirective).NormalizeWhitespace();

                var libModelUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Role.LibNamespace}.Models"));
                root = root.AddUsings(libModelUsingDirective).NormalizeWhitespace();
            }

            // Add Using for Models
            // TODO: i.Output?.Count() -> Implement a Property "IsScalar" and "IsJson"
            if (storedProcedures.Any(i => i.ReadWriteKind == Definition.ReadWriteKindEnum.Read && i.Output?.Count() > 1))
            {
                var modelUsingDirective = _configFile.Config.Project.Role.Kind == ERoleKind.Lib
                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Output.Namespace}.Models.{schema.Name}"))
                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Output.Namespace}.DataContext.Models.{schema.Name}"));
                root = root.AddUsings(modelUsingDirective).NormalizeWhitespace();
            }

            // Add Usings for Params
            if (storedProcedures.Any(s => s.Input?.Any(i => i.IsTableType ?? false) ?? false))
            {
                var paramUsingDirective = _configFile.Config.Project.Role.Kind == ERoleKind.Lib
                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Output.Namespace}.Params.{schema.Name}"))
                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Output.Namespace}.DataContext.Params.{schema.Name}"));
                root = root.AddUsings(paramUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
            }

            // Replace Namespace
            if (_configFile.Config.Project.Role.Kind == ERoleKind.Lib)
            {
                root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", _configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
            }
            else
            {
                root = root.ReplaceNamespace(ns => ns.Replace("Source", _configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
            }

            // Replace ClassName
            root = root.ReplaceClassName(ci => ci.Replace("StoredProcedure", first.EntityName));

            var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            // Generate Methods
            foreach (var storedProcedure in storedProcedures)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var methodNode = (MethodDeclarationSyntax)classNode.Members[0];

                // Replace MethodName
                var methodIdentifier = SyntaxFactory.ParseToken($"{storedProcedure.Name}Async");
                methodNode = methodNode.WithIdentifier(methodIdentifier);

                var withUserId = _configFile.Config.Project.Identity.Kind == EIdentityKind.WithUserId;

                if (withUserId && (storedProcedure.Input.Count() < 1 || storedProcedure.Input.First().Name != "@UserId"))
                {
                    throw new InvalidOperationException($"The StoredProcedure `{storedProcedure.Name}` requires a first Parameter with Name `@UserId`");
                }

                // Generate Method params
                var parameters = storedProcedure.Input.Skip(withUserId ? 1 : 0).Select(input =>
                {
                    return SyntaxFactory.Parameter(SyntaxFactory.Identifier(GetIdentifierFromSqlInputParam(input.Name)))
                        .WithType(
                            input.IsTableType ?? false
                            ? GetInputTypeForTableType(storedProcedure, input)
                            : ParseTypeFromSqlDbTypeName(input.SqlTypeName, input.IsNullable ?? false)
                        )
                        .NormalizeWhitespace()
                        .WithLeadingTrivia(SyntaxFactory.Space);
                });
                var parameterList = methodNode.ParameterList;
                parameterList = parameterList.WithParameters(
                    withUserId
                    ? parameterList.Parameters.InsertRange(3, parameters).RemoveAt(2) // remove tableType
                    : parameterList.Parameters.InsertRange(3, parameters).RemoveAt(1).RemoveAt(1) // remove userId and tableType
                );
                methodNode = methodNode.WithParameterList(parameterList);

                // Get Method Body as Statements
                var methodBody = methodNode.Body;
                var statements = methodBody.Statements.ToList();

                // Generate Sql-Parameters
                var sqlParamSyntax = (LocalDeclarationStatementSyntax)statements.Single(i => i is LocalDeclarationStatementSyntax);
                var sqlParamSyntaxIndex = statements.IndexOf(sqlParamSyntax);

                var arguments = new List<SyntaxNodeOrToken>();
                storedProcedure.Input.ToList().ForEach(i =>
                {
                    arguments.Add(SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("AppDbContext"),
                                SyntaxFactory.IdentifierName((i.IsTableType ?? false) ? "GetCollectionParameter" : "GetParameter")))
                            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                new SyntaxNodeOrToken[]
                                {
                                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(i.Name.Remove(0, 1)))),
                                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GetIdentifierFromSqlInputParam(i.Name)))
                                }))));
                    arguments.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
                });

                statements[sqlParamSyntaxIndex] = SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("parameters"))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(
                            SyntaxFactory.ObjectCreationExpression(
                                SyntaxFactory.GenericName(
                                    SyntaxFactory.Identifier("List"))
                                        .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList<TypeSyntax>(SyntaxFactory.IdentifierName("SqlParameter")))))
                                        .WithInitializer(SyntaxFactory.InitializerExpression(SyntaxKind.CollectionInitializerExpression,
                                            SyntaxFactory.SeparatedList<ExpressionSyntax>(arguments))))))))
                    .NormalizeWhitespace()
                    .WithLeadingTrivia(SyntaxFactory.Tab, SyntaxFactory.Tab, SyntaxFactory.Tab)
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturn);

                methodBody = methodBody.WithStatements(new SyntaxList<StatementSyntax>(statements.Skip(withUserId ? 1 : 2)));
                methodNode = methodNode.WithBody(methodBody);

                // Replace ReturnType and ReturnLine
                var returnType = "Task<CrudResult>";
                var returnExpression = $"context.ExecuteSingleAsync<CrudResult>(\"{storedProcedure.SqlObjectName}\", parameters, cancellationToken, transaction)";
                var returnModel = "CrudResult";

                // TODO: i.Output?.Count() -> Implement a Property "IsScalar" and "IsJson"
                var isScalar = storedProcedure.Output?.Count() == 1;
                if (isScalar)
                {
                    var output = storedProcedure.Output.FirstOrDefault();
                    returnModel = ParseTypeFromSqlDbTypeName(output.SqlTypeName, output.IsNullable).ToString();

                    returnType = $"Task<{returnModel}>";
                    returnExpression = returnExpression.Replace("ExecuteSingleAsync<CrudResult>", $"ReadJsonAsync");
                }
                else
                {
                    switch (storedProcedure.OperationKind)
                    {
                        case Definition.OperationKindEnum.FindBy:
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

                methodBody = methodBody.WithStatements(new SyntaxList<StatementSyntax>(statements));
                methodNode = methodNode.WithBody(methodBody);

                root = root.AddMethod(classNode, methodNode);
            }

            // Remove template Method
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            root = root.ReplaceNode(classNode, classNode.WithMembers(new SyntaxList<MemberDeclarationSyntax>(classNode.Members.Cast<MethodDeclarationSyntax>().Skip(1))));

            return root.NormalizeWhitespace().GetText();
        }

        public void GenerateDataContextStoredProcedures(bool dryrun)
        {
            var schemas = _configFile.Config.Schema
                .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
                .Select(i => Definition.ForSchema(i));

            foreach (var schema in schemas)
            {
                var storedProcedures = schema.StoredProcedures;

                if (!(storedProcedures.Any()))
                {
                    continue;
                }

                var dataContextStoredProcedurePath = DirectoryUtils.GetWorkingDirectory(_configFile.Config.Project.Output.DataContext.Path, _configFile.Config.Project.Output.DataContext.StoredProcedures.Path);
                var path = Path.Combine(dataContextStoredProcedurePath, schema.Path);
                if (!Directory.Exists(path) && !dryrun)
                {
                    Directory.CreateDirectory(path);
                }

                foreach (var groupedStoredProcedures in storedProcedures.GroupBy(i => i.EntityName, (key, group) => group.ToList()))
                {
                    var first = groupedStoredProcedures.First();
                    var fileName = Path.Combine(path, $"{first.EntityName}Extensions.cs");
                    var sourceText = GetStoredProcedureText(schema, groupedStoredProcedures);

                    if (ExistingFileMatches(fileName, sourceText))
                    {
                        // Existing StoredProcedure and new StoredProcedure matches
                        continue;
                    }

                    if (!dryrun)
                        File.WriteAllText(fileName, sourceText.WithMetadataToString(_spocr.Version));
                }
            }
        }
    }
}
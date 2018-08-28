using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SpocR.Internal.Common;
using SpocR.Internal.Extensions;
using SpocR.Internal.Models;
using static SpocR.Internal.Common.Definitions;

namespace SpocR.Internal.Common
{
    internal class Engine
    {
        internal readonly IServiceCollection Services;

        private ConfigurationModel _config;
        internal ConfigurationModel Config
        {
            get => _config ?? (_config = ReadConfigFile());
            set => _config = value;
        }

        internal Engine(IServiceCollection services)
        {
            Services = services;
        }

        internal bool ConfigFileExists()
        {
            return File.Exists(Configuration.ConfigurationFile);
        }

        internal void SaveConfigFile(ConfigurationModel config)
        {
            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new SerializeContractResolver()
            };
            var json = JsonConvert.SerializeObject(new ConfigurationJsonModel(config), Formatting.Indented, jsonSettings);
            File.WriteAllText(Configuration.ConfigurationFile, json);
        }

        internal ConfigurationModel ReadConfigFile()
        {
            if (!ConfigFileExists())
            {
                return null;
            }
            var content = File.ReadAllText(Configuration.ConfigurationFile);
            return JsonConvert.DeserializeObject<ConfigurationModel>(content);
        }

        public string GetApplicationRoot()
        {
            var codeBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase);
            return Regex.Replace(codeBase, @"^(file\:\\)", string.Empty);
        }

        internal DirectoryInfo GetSourceStructureRootDir()
        {
            return new DirectoryInfo(Path.Combine(GetApplicationRoot(), "Internal", "SourceStructure"));
        }

        internal IEnumerable<StructureModel> GetStructureModelListFromSource(DirectoryInfo rootDir = null, string parentPath = null)
        {
            rootDir = rootDir ?? GetSourceStructureRootDir();
            foreach (var child in rootDir.GetDirectories())
            {
                var path = $"{parentPath ?? "."}/{child.Name}";
                yield return new StructureModel
                {
                    Name = child.Name,
                    Path = path,
                    Children = GetStructureModelListFromSource(child, path)
                };
            }
        }

        private StructureModel GetStructureNodeBySourcePath(string path)
        {
            var info = new DirectoryInfo(path);
            var rootDir = GetSourceStructureRootDir();
            var relativePath = info.FullName.Replace(rootDir.FullName, "");

            var directories = relativePath
                                .Split(Path.DirectorySeparatorChar)
                                .Where(i => !string.IsNullOrWhiteSpace(i));

            var strutureNode = directories.Any()
                ? Config.Project.Structure.SingleOrDefault(i => i.Name.Equals(directories.First()))
                : null;

            foreach (var dirName in directories.Skip(1))
            {
                strutureNode = strutureNode.Children.SingleOrDefault(i => i.Name.Equals(dirName));
            }

            return strutureNode;
        }

        // https://docs.microsoft.com/de-de/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis
        internal void GenerateCodeBase()
        {

            var rootDir = GetSourceStructureRootDir();
            var baseFiles = rootDir.GetFiles("*.base.cs", SearchOption.AllDirectories);

            foreach (var file in baseFiles)
            {
                var fileContent = File.ReadAllText(file.FullName);

                var tree = CSharpSyntaxTree.ParseText(fileContent);
                var root = tree.GetCompilationUnitRoot();

                // Replace Namespace
                var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                var name = SyntaxFactory.ParseName($"{nsNode.Name.ToString().Replace("Source", Config.Project.Namespace)}{Environment.NewLine}");
                root = root.ReplaceNode(nsNode, nsNode.WithName(name));

                var targetDir = Path.Combine(Directory.GetCurrentDirectory(), GetStructureNodeBySourcePath(file.DirectoryName).Path);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                File.WriteAllText(Path.Combine(targetDir, file.Name.Replace(".base.cs", ".cs")), root.GetText().ToString());
            }
        }

        internal StructureModel GetCustomStructure(params string[] names)
        {
            return GetCustomStructure(Config.Project.Structure, names);
        }

        internal StructureModel GetCustomStructure(IEnumerable<StructureModel> structures = null, params string[] names)
        {
            return names.Length > 1
                    ? GetCustomStructure(structures.Single(i => i.Name.Equals(names.First())).Children, names.Skip(1).ToArray())
                    : structures.Single(i => i.Name.Equals(names.First()));
        }

        internal TypeSyntax ParseTypeFromSqlDbTypeName(string sqlTypeName, bool isNullable)
        {
            sqlTypeName = sqlTypeName.Split('(')[0];
            var sqlType = (SqlDbType)Enum.Parse(typeof(SqlDbType), sqlTypeName, true);
            var clrType = SqlDbHelper.GetType(sqlType, isNullable);
            return SyntaxFactory.ParseTypeName(clrType.ToGenericTypeString());
        }

        internal SourceText GetModelTextForStoredProcedure(SchemaDefinition schema, StoredProcedureDefinition storedProcedure)
        {
            var rootDir = GetSourceStructureRootDir();
            var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "Models", "Model.cs"));

            var tree = CSharpSyntaxTree.ParseText(fileContent);
            var root = tree.GetCompilationUnitRoot();

            // Replace Namespace
            var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var fullSchemaName = SyntaxFactory.ParseName($"{nsNode.Name.ToString().Replace("Source", Config.Project.Namespace).Replace("Schema", schema.Name)}{Environment.NewLine}");
            root = root.ReplaceNode(nsNode, nsNode.WithName(fullSchemaName));

            // Replace ClassName
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            var classIdentifier = SyntaxFactory.ParseToken($"{classNode.Identifier.ValueText.Replace("Model", storedProcedure.Name)}{Environment.NewLine}");
            root = root.ReplaceNode(classNode, classNode.WithIdentifier(classIdentifier));

            // Generate Properies
            // https://stackoverflow.com/questions/45160694/adding-new-field-declaration-to-class-with-roslyn
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

        internal void GenerateDataContextModels()
        {
            var schemas = Config.Schema
                .Where(i => i.StoredProcedures?.Any() ?? false)
                .Select(i => Definitions.ForSchema(i));

            foreach (var schema in schemas)
            {
                var storedProcedures = schema.StoredProcedures
                    .Where(i => i.ReadWriteKind == ReadWriteKindEnum.Read);

                if (!(storedProcedures.Any()))
                {
                    continue;
                }

                var dataContextModelPath = GetCustomStructure("DataContext", "Models").Path;
                var path = Path.Combine(dataContextModelPath, schema.Path);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                foreach (var storedProcedure in storedProcedures)
                {
                    var fileName = Path.Combine(path, $"{storedProcedure.Name}.cs");
                    var sourceText = GetModelTextForStoredProcedure(schema, storedProcedure);

                    if (ExistingModelFileMatching(fileName, sourceText)) {
                        // Existing Model and new Model matching
                        continue;
                    }

                    File.WriteAllText(fileName, sourceText.WithMetadataToString());
                }
            }
        }

        private bool ExistingModelFileMatching(string fileName, SourceText sourceText)
        {
            if(File.Exists(fileName)) {
                var oldFileContent = File.ReadAllText(fileName);
                var oldTree = CSharpSyntaxTree.ParseText(oldFileContent);
                var oldRoot = oldTree.GetCompilationUnitRoot();
                var oldNsNode = (NamespaceDeclarationSyntax)oldRoot.Members[0];

                var newTree = CSharpSyntaxTree.ParseText(sourceText);
                var newRoot = newTree.GetCompilationUnitRoot();
                var newNsNode = (NamespaceDeclarationSyntax)newRoot.Members[0];

                if(oldNsNode.GetText().ToString().Equals(newNsNode.GetText().ToString())) {
                    return true;
                }
            }
            return false;
        }

        internal string GetStoredProcedureText(SchemaDefinition schema, List<StoredProcedureDefinition> storedProcedures)
        {
            var first = storedProcedures.First();
            var rootDir = GetSourceStructureRootDir();
            var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "StoredProcedures", "StoredProcedureExtensions.cs"));

            var tree = CSharpSyntaxTree.ParseText(fileContent);
            var root = tree.GetCompilationUnitRoot();

            // Replace Usings
            for (var i = 0; i < root.Usings.Count; i++)
            {
                var usingDirective = root.Usings[i];
                var newUsingName = SyntaxFactory.ParseName(
                    $"{usingDirective.Name.ToString().Replace("Source", Config.Project.Namespace)}");
                root = root.ReplaceNode(usingDirective, usingDirective.WithName(newUsingName));
            }

            // Add Using for Models
            if (storedProcedures.Any(i => i.ReadWriteKind == ReadWriteKindEnum.Read))
            {
                var modelUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{Config.Project.Namespace}.DataContext.Models.{schema.Name}"));
                root = root.AddUsings(modelUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed));
            }

            // Replace Namespace
            var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var fullSchemaName = SyntaxFactory.ParseName(
                $"{nsNode.Name.ToString().Replace("Source", Config.Project.Namespace).Replace("Schema", schema.Name)}{Environment.NewLine}");
            root = root.ReplaceNode(nsNode, nsNode.WithName(fullSchemaName));

            // Replace ClassName
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            var classIdentifier = SyntaxFactory.ParseToken($"{classNode.Identifier.ValueText.Replace("StoredProcedure", first.EntityName)}{Environment.NewLine}");
            root = root.ReplaceNode(classNode, classNode.WithIdentifier(classIdentifier));

            // Generate Methods
            foreach (var storedProcedure in storedProcedures)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];
                var methodNode = (MethodDeclarationSyntax)classNode.Members[0];

                // Replace MethodName
                var methodIdentifier = SyntaxFactory.ParseToken($"{storedProcedure.Name}Async");
                methodNode = methodNode.WithIdentifier(methodIdentifier);

                // Generate Method params
                var parameters = storedProcedure.Input.Skip(1).Select(i =>
                {
                    return SyntaxFactory.Parameter(SyntaxFactory.Identifier($"{i.Name.Remove(0, 1).FirstCharToLower()}"))
                        .WithType(ParseTypeFromSqlDbTypeName(i.SqlTypeName, i.IsNullable))
                        .NormalizeWhitespace()
                        .WithLeadingTrivia(SyntaxFactory.Space);
                });
                var parameterList = methodNode.ParameterList;
                parameterList = parameterList.WithParameters(parameterList.Parameters.InsertRange(2, parameters));
                methodNode = methodNode.WithParameterList(parameterList);

                // Get Method Body as Statements
                var methodBody = methodNode.Body;
                var statements = methodBody.Statements.ToList();

                // Generate Sql-Parameters
                var sqlParamSyntax = (LocalDeclarationStatementSyntax)statements.Single(i => i is LocalDeclarationStatementSyntax);
                var sqlParamSyntaxIndex = statements.IndexOf(sqlParamSyntax);

                var arguments = new List<SyntaxNodeOrToken>();
                storedProcedure.Input.ToList().ForEach(i => {
                    arguments.Add(SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("AppDbContext"), SyntaxFactory.IdentifierName("GetParameter")))
                            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                new SyntaxNodeOrToken[]
                                {
                                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(i.Name.Remove(0, 1)))),
                                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(i.Name.Remove(0, 1).FirstCharToLower()))
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

                methodBody = methodBody.WithStatements(new SyntaxList<StatementSyntax>(statements));
                methodNode = methodNode.WithBody(methodBody);

                // Replace ReturnType and ReturnLine
                var returnType = "Task<CrudResult>";
                var returnExpression = $"context.ExecuteSingleAsync<CrudResult>(\"{storedProcedure.SqlObjectName}\", parameters, cancellationToken, transaction)";
                var returnModel = "CrudResult";

                switch (storedProcedure.OperationKind)
                {
                    case OperationKindEnum.FindBy:
                    case OperationKindEnum.List:
                        returnModel = storedProcedure.Name;
                        break;
                }

                switch (storedProcedure.ResultKind)
                {
                    case ResultKindEnum.Single:
                        returnType = $"Task<{returnModel}>";
                        returnExpression = returnExpression.Replace("ExecuteSingleAsync<CrudResult>", $"ExecuteSingleAsync<{returnModel}>");
                        break;
                    case ResultKindEnum.List:
                        returnType = $"Task<List<{returnModel}>>";
                        returnExpression = returnExpression.Replace("ExecuteSingleAsync<CrudResult>", $"ExecuteListAsync<{returnModel}>");
                        break;
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

            return root.GetText().WithMetadataToString();
        }

        internal void GenerateDataContextStoredProcedures()
        {
            var schemas = Config.Schema
                .Where(i => i.StoredProcedures?.Any() ?? false)
                .Select(i => Definitions.ForSchema(i));

            foreach (var schema in schemas)
            {
                var storedProcedures = schema.StoredProcedures;

                if (!(storedProcedures.Any()))
                {
                    continue;
                }

                var dataContextStoredProcedurePath = GetCustomStructure("DataContext", "StoredProcedures").Path;
                var path = Path.Combine(dataContextStoredProcedurePath, schema.Path);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                foreach (var groupedStoredProcedures in storedProcedures.GroupBy(i => i.EntityName, (key, group) => group.ToList()))
                {
                    var first = groupedStoredProcedures.First();
                    var fileName = Path.Combine(path, $"{first.EntityName}Extensions.cs");
                    var codeText = GetStoredProcedureText(schema, groupedStoredProcedures);
                    File.WriteAllText(fileName, codeText);
                }
            }
        }

        internal void RemoveGeneratedFiles(IEnumerable<StructureModel> structures = null)
        {
            structures = structures ?? Config.Project.Structure;
            foreach (var structure in structures)
            {
                if (Directory.Exists(structure.Path))
                {
                    Directory.Delete(structure.Path, true);
                }
            }
        }

        internal void RemoveConfig()
        {
            if (File.Exists(Configuration.ConfigurationFile))
            {
                File.Delete(Configuration.ConfigurationFile);
            }
        }
    }

    internal static class SpocRServiceCollectionExtensions
    {
        internal static IServiceCollection AddSpocR(this IServiceCollection services)
        {
            services.AddSingleton(new Engine(services));
            return services;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
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
        private readonly IReportService _reportService;

        public Generator(FileManager<ConfigurationModel> configFile, SpocrService spocr, OutputService output, IReportService reportService)
        {
            _configFile = configFile;
            _spocr = spocr;
            _output = output;
            _reportService = reportService;
        }

        public TypeSyntax ParseTypeFromSqlDbTypeName(string sqlTypeName, bool isNullable)
        {
            // temporary for #56: we should not abort execution if config is corrupt
            if (string.IsNullOrEmpty(sqlTypeName))
            {
                _reportService.PrintCorruptConfigMessage($"Could not parse 'SqlTypeName' - setting the type to dynamic");
                sqlTypeName = "Variant";
            }

            sqlTypeName = sqlTypeName.Split('(')[0];
            var sqlType = (SqlDbType)Enum.Parse(typeof(SqlDbType), sqlTypeName, true);
            var clrType = SqlDbHelper.GetType(sqlType, isNullable);
            return SyntaxFactory.ParseTypeName(clrType.ToGenericTypeString());
        }

        public TypeSyntax GetTypeSyntaxForTableType(string storedProcedureName, string propertyName)
        {
            return SyntaxFactory.ParseTypeName($"{storedProcedureName}{propertyName}");
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

        public SourceText GetInputTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
        {
            var rootDir = _output.GetOutputRootDir();
            var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "Inputs", "Input.cs"));

            var tree = CSharpSyntaxTree.ParseText(fileContent);
            var root = tree.GetCompilationUnitRoot();

            // If Inputs contains a tableType, add using for Params
            if (storedProcedure.Input.Any(_ => _.IsTableType ?? false))
            {
                var paramUsingDirective = _configFile.Config.Project.Role.Kind == ERoleKind.Lib
                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Output.Namespace}.Params.{schema.Name}"))
                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Output.Namespace}.DataContext.Params.{schema.Name}"));
                root = root.AddUsings(paramUsingDirective);
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
            root = root.ReplaceClassName(ci => ci.Replace("Input", $"{storedProcedure.Name}Input"));

            // Generate Properies
            // https://stackoverflow.com/questions/45160694/adding-new-field-declaration-to-class-with-roslyn
            var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];
            foreach (var item in storedProcedure.Input)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];

                var propertyIdentifier = SyntaxFactory.ParseToken($" {item.Name.Replace("@", "")} ");

                if (item.IsTableType ?? false)
                {
                    propertyNode = propertyNode
                        .WithType(GetTypeSyntaxForTableType(storedProcedure.Name, item.Name.Replace("@", "")));
                }
                else
                {
                    propertyNode = propertyNode
                        .WithType(ParseTypeFromSqlDbTypeName(item.SqlTypeName, item.IsNullable ?? false));
                }

                propertyNode = propertyNode
                    .WithIdentifier(propertyIdentifier).NormalizeWhitespace();

                root = root.AddProperty(classNode, propertyNode);
            }

            // Remove template Property
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            root = root.ReplaceNode(classNode, classNode.WithMembers(new SyntaxList<MemberDeclarationSyntax>(classNode.Members.Cast<PropertyDeclarationSyntax>().Skip(1))));

            return root.NormalizeWhitespace().GetText();
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
                    .WithType(ParseTypeFromSqlDbTypeName(item.SqlTypeName, item.IsNullable ?? false));

                propertyNode = propertyNode
                    .WithIdentifier(propertyIdentifier);

                root = root.AddProperty(classNode, propertyNode);
            }

            // Remove template Property
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            root = root.ReplaceNode(classNode, classNode.WithMembers(new SyntaxList<MemberDeclarationSyntax>(classNode.Members.Cast<PropertyDeclarationSyntax>().Skip(1))));

            return root.NormalizeWhitespace().GetText();
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

            return root.NormalizeWhitespace().GetText();
        }

        public void GenerateDataContextParams(bool isDryRun)
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
                if (!Directory.Exists(path) && !isDryRun)
                {
                    Directory.CreateDirectory(path);
                }

                foreach (var storedProcedure in storedProcedures)
                {
                    var fileName = $"{storedProcedure.Name}Params.cs";
                    var fileNameWithPath = Path.Combine(path, fileName);
                    var sourceText = GetParamsTextForStoredProcedure(schema, storedProcedure);

                    _output.Write(fileNameWithPath, sourceText, isDryRun);
                }
            }
        }

        public void GenerateDataContextInputs(bool isDryRun)
        {
            // Migrate to Version 1.3.2
            if (_configFile.Config.Project.Output.DataContext.Inputs == null)
            {
                var defaultConfig = _spocr.GetDefaultConfiguration();
                _configFile.Config.Project.Output.DataContext.Inputs = defaultConfig.Project.Output.DataContext.Inputs;
            }

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

                var dataContextInputPath = DirectoryUtils.GetWorkingDirectory(_configFile.Config.Project.Output.DataContext.Path, _configFile.Config.Project.Output.DataContext.Inputs.Path);
                var path = Path.Combine(dataContextInputPath, schema.Path);
                if (!Directory.Exists(path) && !isDryRun)
                {
                    Directory.CreateDirectory(path);
                }

                foreach (var storedProcedure in storedProcedures)
                {
                    if (!(storedProcedure.Input?.Any() ?? false))
                    {
                        continue;
                    }
                    var fileName = $"{storedProcedure.Name}.cs";
                    var fileNameWithPath = Path.Combine(path, fileName);
                    var sourceText = GetInputTextForStoredProcedure(schema, storedProcedure);

                    _output.Write(fileNameWithPath, sourceText, isDryRun);
                }
            }
        }

        public void GenerateDataContextModels(bool isDryRun)
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
                if (!Directory.Exists(path) && !isDryRun)
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
                    var fileName = $"{storedProcedure.Name}.cs";
                    var fileNameWithPath = Path.Combine(path, fileName);
                    var sourceText = GetModelTextForStoredProcedure(schema, storedProcedure);

                    _output.Write(fileNameWithPath, sourceText, isDryRun);
                }
            }
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

        public string GetPropertyFromSqlInputParam(string name)
        {
            name = $"{name.Remove(0, 1).FirstCharToUpper()}";
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

            // Add Usings for Inputs
            if (storedProcedures.Any(s => s.Input?.Any() ?? false))
            {
                var inputUsingDirective = _configFile.Config.Project.Role.Kind == ERoleKind.Lib
                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Output.Namespace}.Inputs.{schema.Name}"))
                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{_configFile.Config.Project.Output.Namespace}.DataContext.Inputs.{schema.Name}"));
                root = root.AddUsings(inputUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
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

                // Origin
                var originMethodNode = (MethodDeclarationSyntax)classNode.Members[0];
                originMethodNode = GenerateStoredProcedureMethodText(originMethodNode, storedProcedure);
                root = root.AddMethod(classNode, originMethodNode);

                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[0];

                // Overloaded with IExecuteOptions
                var overloadOptionsMethodNode = (MethodDeclarationSyntax)classNode.Members[1];
                overloadOptionsMethodNode = GenerateStoredProcedureMethodText(overloadOptionsMethodNode, storedProcedure, true);
                root = root.AddMethod(classNode, overloadOptionsMethodNode);
            }

            // Remove template Method
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];
            root = root.ReplaceNode(classNode, classNode.WithMembers(new SyntaxList<MemberDeclarationSyntax>(classNode.Members.Cast<MethodDeclarationSyntax>().Skip(2))));

            return root.NormalizeWhitespace().GetText();
        }

        private MethodDeclarationSyntax GenerateStoredProcedureMethodText(MethodDeclarationSyntax methodNode, Definition.StoredProcedure storedProcedure, bool useInputModel = false)
        {
            // Replace MethodName
            var methodIdentifier = SyntaxFactory.ParseToken($"{storedProcedure.Name}Async");
            methodNode = methodNode.WithIdentifier(methodIdentifier);

            var withUserId = _configFile.Config.Project.Identity.Kind == EIdentityKind.WithUserId;
            var userIdExists = storedProcedure.Input.Any() && storedProcedure.Input.First().Name == "@UserId";
            
            if (withUserId && !userIdExists)
            {
                // ? This is just to prevent follow-up issues, as long as the architecture handles SPs like this
                withUserId = false;

                _reportService.Warn(
                    new StringBuilder()
                    .Append($"The StoredProcedure {storedProcedure.SqlObjectName} violates the requirement: ")
                    .Append("First Parameter with Name '@UserId'")
                    .Append(" (this can lead to unpredictable issues)")
                    .ToString()
                );
                // throw new InvalidOperationException($"The StoredProcedure `{storedProcedure.Name}` requires a first Parameter with Name `@UserId`");
            }

            // Generate Method params
            var parameters = useInputModel
                                    ? new[] { SyntaxFactory.Parameter(SyntaxFactory.Identifier("model"))
                                                .WithType(SyntaxFactory.ParseTypeName($"{storedProcedure.Name}Input"))
                                                .NormalizeWhitespace()
                                                .WithLeadingTrivia(SyntaxFactory.Space) }
                                    : storedProcedure.Input.Skip(withUserId && userIdExists ? 1 : 0).Select(input =>
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
                withUserId && !useInputModel
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
                                    SyntaxFactory.Argument(
                                        SyntaxFactory.IdentifierName(
                                        useInputModel
                                        ? $"model.{GetPropertyFromSqlInputParam(i.Name)}"
                                        : GetIdentifierFromSqlInputParam(i.Name)
                                        ))
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

            methodBody = methodBody.WithStatements(new SyntaxList<StatementSyntax>(statements.Skip(withUserId && !useInputModel ? 1 : 2)));
            methodNode = methodNode.WithBody(methodBody);

            // Replace ReturnType and ReturnLine
            var returnType = "Task<CrudResult>";
            //var returnExpression = $"context.ExecuteSingleAsync<CrudResult>(\"{storedProcedure.SqlObjectName}\", parameters, cancellationToken, transaction)";
            var returnExpression = (statements.Last() as ReturnStatementSyntax).Expression.GetText().ToString().Replace("schema.CrudAction", storedProcedure.SqlObjectName);
            var returnModel = "CrudResult";

            // TODO: i.Output?.Count() -> Implement a Property "IsScalar" and "IsJson"
            var isScalar = storedProcedure.Output?.Count() == 1;
            if (isScalar)
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

            return methodNode;
        }

        public void GenerateDataContextStoredProcedures(bool isDryRun)
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
                if (!Directory.Exists(path) && !isDryRun)
                {
                    Directory.CreateDirectory(path);
                }

                foreach (var groupedStoredProcedures in storedProcedures.GroupBy(i => i.EntityName, (key, group) => group.ToList()))
                {
                    var first = groupedStoredProcedures.First();

                    var fileName = $"{first.EntityName}Extensions.cs";
                    var fileNameWithPath = Path.Combine(path, fileName);

                    var sourceText = GetStoredProcedureText(schema, groupedStoredProcedures);

                    _output.Write(fileNameWithPath, sourceText, isDryRun);
                }
            }
        }
    }
}
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
using SpocR.Roslyn.Helpers;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR;

public class Generator(
    FileManager<ConfigurationModel> configFile,
    SpocrService spocr,
    OutputService output,
    IReportService reportService
)
{
    public TypeSyntax ParseTypeFromSqlDbTypeName(string sqlTypeName, bool isNullable)
    {
        // temporary for #56: we should not abort execution if config is corrupt
        if (string.IsNullOrEmpty(sqlTypeName))
        {
            reportService.PrintCorruptConfigMessage($"Could not parse 'SqlTypeName' - setting the type to dynamic");
            sqlTypeName = "Variant";
        }

        sqlTypeName = sqlTypeName.Split('(')[0];
        var sqlType = Enum.Parse<SqlDbType>(sqlTypeName, true);
        var clrType = SqlDbHelper.GetType(sqlType, isNullable);
        return SyntaxFactory.ParseTypeName(clrType.ToGenericTypeString());
    }

    public static string GetTypeNameForTableType(Definition.TableType tableType)
    {
        return $"{tableType.Name}";
    }

    public static TypeSyntax GetTypeSyntaxForTableType(StoredProcedureInputModel input)
    {
        return input.Name.EndsWith("List")
            ? SyntaxFactory.ParseTypeName($"IEnumerable<{input.TableTypeName}>")
            : SyntaxFactory.ParseTypeName($"{input.TableTypeName}");
    }

    public SourceText GetInputTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        var rootDir = output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "Inputs", "Input.cs"));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // If Inputs contains a TableType, add using for TableTypes
        var schemesForTableTypes = storedProcedure.Input.Where(i => i.IsTableType ?? false)
                             .GroupBy(t => (t.TableTypeSchemaName, t.TableTypeName), (key, group) => new
                             {
                                 TableTypeSchemaName = key.TableTypeSchemaName,
                                 Result = group.First()
                             }).Select(g => g.Result).ToList();

        var tableTypeSchemas = storedProcedure.Input.Where(i => i.IsTableType ?? false)
                                    .GroupBy(t => t.TableTypeSchemaName, (key, group) => key).ToList();

        foreach (var tableTypeSchema in tableTypeSchemas)
        {

            var tableTypeSchemaConfig = configFile.Config.Schema.Find(s => s.Name.Equals(tableTypeSchema));
            // is schema of table type ignored and its an extension?
            var useFromLib = tableTypeSchemaConfig?.Status != SchemaStatusEnum.Build
                && configFile.Config.Project.Role.Kind == ERoleKind.Extension;

            var paramUsingDirective = useFromLib
                                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Role.LibNamespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                                : configFile.Config.Project.Role.Kind == ERoleKind.Lib
                                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.DataContext.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
            root = root.AddUsings(paramUsingDirective);
        }

        // Replace Namespace
        if (configFile.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }

        // Replace ClassName
        root = root.ReplaceClassName(ci => ci.Replace("Input", $"{storedProcedure.Name}Input"));
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Create obsolete constructor
        var obsoleteContructor = classNode.CreateConstructor($"{storedProcedure.Name}Input");
        root = root.AddObsoleteAttribute(ref obsoleteContructor, "This empty contructor will be removed in vNext. Please use constructor with parameters.");
        root = root.AddConstructor(ref classNode, obsoleteContructor);
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        var inputs = storedProcedure.Input.Where(i => !i.IsOutput).ToList();
        // Constructor with params
        var constructor = classNode.CreateConstructor($"{storedProcedure.Name}Input");
        var parameters = inputs.Select(input =>
        {
            return SyntaxFactory.Parameter(SyntaxFactory.Identifier(GetIdentifierFromSqlInputTableType(input.Name)))
                .WithType(
                    input.IsTableType ?? false
                    ? GetTypeSyntaxForTableType(input)
                    : ParseTypeFromSqlDbTypeName(input.SqlTypeName, input.IsNullable ?? false)
                );
        }).ToArray();

        var constructorParams = constructor.ParameterList.AddParameters(parameters);
        constructor = constructor.WithParameterList(constructorParams);

        foreach (var input in inputs)
        {
            var constructorStatement = ExpressionHelper.AssignmentStatement(TokenHelper.Parse(input.Name).ToString(), GetIdentifierFromSqlInputTableType(input.Name));
            var newStatements = constructor.Body.Statements.Add(constructorStatement);
            constructor = constructor.WithBody(constructor.Body.WithStatements(newStatements));
        }

        root = root.AddConstructor(ref classNode, constructor);
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Generate Properies
        // https://stackoverflow.com/questions/45160694/adding-new-field-declaration-to-class-with-roslyn

        foreach (var item in storedProcedure.Input)
        {
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            var isTableType = item.IsTableType ?? false;
            var propertyType = isTableType
                ? GetTypeSyntaxForTableType(item)
                : ParseTypeFromSqlDbTypeName(item.SqlTypeName, item.IsNullable ?? false);

            var propertyNode = classNode.CreateProperty(propertyType, item.Name);

            if (!isTableType)
            {
                // Add Attribute for NVARCHAR with MaxLength
                if ((item.SqlTypeName?.Equals(SqlDbType.NVarChar.ToString(), StringComparison.InvariantCultureIgnoreCase) ?? false)
                    && item.MaxLength.HasValue)
                {
                    var attributes = propertyNode.AttributeLists.Add(
                        SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                            SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("MaxLength"), SyntaxFactory.ParseAttributeArgumentList($"({item.MaxLength})"))
                        )).NormalizeWhitespace());

                    propertyNode = propertyNode.WithAttributeLists(attributes);
                }
            }

            root = root.AddProperty(ref classNode, propertyNode);
        }

        return root.NormalizeWhitespace().GetText();
    }

    public SourceText GetModelTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        var rootDir = output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "Models", "Model.cs"));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // Replace Namespace
        if (configFile.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }

        // Replace ClassName
        root = root.ReplaceClassName(ci => ci.Replace("Model", storedProcedure.Name));

        // Generate Properies
        // https://stackoverflow.com/questions/45160694/adding-new-field-declaration-to-class-with-roslyn
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];
        var outputs = storedProcedure.Output?.ToList() ?? [];
        foreach (var item in outputs)
        {
            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            var propertyIdentifier = SyntaxFactory.ParseToken($" {item.Name.FirstCharToUpper()} ");
            propertyNode = propertyNode
                .WithType(ParseTypeFromSqlDbTypeName(item.SqlTypeName, item.IsNullable ?? false));

            propertyNode = propertyNode
                .WithIdentifier(propertyIdentifier);

            root = root.AddProperty(ref classNode, propertyNode);
        }

        // Remove template Property
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        root = root.ReplaceNode(classNode, classNode.WithMembers([.. classNode.Members.Cast<PropertyDeclarationSyntax>().Skip(1)]));

        return root.NormalizeWhitespace().GetText();
    }

    public SourceText GetOutputTextForStoredProcedure(Definition.Schema schema, Definition.StoredProcedure storedProcedure)
    {
        var rootDir = output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "Outputs", "Output.cs"));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // Add Usings
        if (configFile.Config.Project.Role.Kind == ERoleKind.Extension)
        {
            var outputUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Role.LibNamespace}.Outputs"));
            root = root.AddUsings(outputUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        // Replace Namespace
        if (configFile.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }

        // Replace ClassName
        root = root.ReplaceClassName(ci => ci.Replace("Output", storedProcedure.GetOutputTypeName()));

        // Generate Properties
        // https://stackoverflow.com/questions/45160694/adding-new-field-declaration-to-class-with-roslyn
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];
        var outputs = storedProcedure.Input?.Where(i => i.IsOutput).ToList() ?? [];
        foreach (var output in outputs)
        {
            // do not add properties who exists in base class (IOutput)
            // TODO: parse from IOutput
            var ignoredFields = new[] { "ResultId", "RecordId", "RowVersion" };
            if (Array.IndexOf(ignoredFields, output.Name.Replace("@", "")) > -1) { continue; }

            nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            classNode = (ClassDeclarationSyntax)nsNode.Members[0];

            var propertyIdentifier = TokenHelper.Parse(output.Name);
            propertyNode = propertyNode
                .WithType(ParseTypeFromSqlDbTypeName(output.SqlTypeName, output.IsNullable ?? false));

            propertyNode = propertyNode
                .WithIdentifier(propertyIdentifier);

            root = root.AddProperty(ref classNode, propertyNode);
        }

        // Remove template Property
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        root = root.ReplaceNode(classNode, classNode.WithMembers([.. classNode.Members.Cast<PropertyDeclarationSyntax>().Skip(1)]));

        return root.NormalizeWhitespace().GetText();
    }

    public SourceText GetTableTypeText(Definition.Schema schema, Definition.TableType tableType)
    {
        var rootDir = output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "TableTypes", "TableType.cs"));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // Replace Namespace
        if (configFile.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }

        // If its an extension, add usings for the lib
        if (configFile.Config.Project.Role.Kind == ERoleKind.Extension)
        {
            var libModelUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Role.LibNamespace}.TableTypes"));
            root = root.AddUsings(libModelUsingDirective).NormalizeWhitespace();
        }

        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];

        // Replace ClassName
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];
        var classIdentifier = SyntaxFactory.ParseToken($"{classNode.Identifier.ValueText.Replace("TableType", $"{GetTypeNameForTableType(tableType)}")} ");
        classNode = classNode.WithIdentifier(classIdentifier);

        root = root.ReplaceNode(nsNode, nsNode.AddMembers(classNode));

        // Create Properties
        if (tableType.Columns != null)
        {
            foreach (var column in tableType.Columns)
            {
                nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                classNode = (ClassDeclarationSyntax)nsNode.Members[1];
                var propertyNode = (PropertyDeclarationSyntax)classNode.Members[0];

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

        // Remove template Property
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        classNode = (ClassDeclarationSyntax)nsNode.Members[1];
        root = root.ReplaceNode(classNode, classNode.WithMembers([.. classNode.Members.Cast<PropertyDeclarationSyntax>().Skip(1)]));

        // Remove template Class
        nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        root = root.ReplaceNode(nsNode, nsNode.WithMembers([.. nsNode.Members.Cast<ClassDeclarationSyntax>().Skip(1)]));

        return root.NormalizeWhitespace().GetText();
    }

    public void GenerateDataContextTableTypes(bool isDryRun)
    {
        var schemas = configFile.Config.Schema
            .Where(i => i.TableTypes?.Any() ?? false)
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var tableTypes = schema.TableTypes;

            var dataContextTableTypesPath = DirectoryUtils.GetWorkingDirectory(configFile.Config.Project.Output.DataContext.Path, configFile.Config.Project.Output.DataContext.TableTypes.Path);
            var path = Path.Combine(dataContextTableTypesPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var tableType in tableTypes)
            {
                var fileName = $"{tableType.Name}TableType.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = GetTableTypeText(schema, tableType);

                output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }

    public void GenerateDataContextInputs(bool isDryRun)
    {
        // Migrate to Version 1.3.2
        if (configFile.Config.Project.Output.DataContext.Inputs == null)
        {
            var defaultConfig = spocr.GetDefaultConfiguration();
            configFile.Config.Project.Output.DataContext.Inputs = defaultConfig.Project.Output.DataContext.Inputs;
        }

        var schemas = configFile.Config.Schema
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures;

            if (!storedProcedures.Any())
            {
                continue;
            }

            var dataContextInputPath = DirectoryUtils.GetWorkingDirectory(configFile.Config.Project.Output.DataContext.Path, configFile.Config.Project.Output.DataContext.Inputs.Path);
            var path = Path.Combine(dataContextInputPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var storedProcedure in storedProcedures)
            {
                if (!storedProcedure.HasInputs())
                {
                    continue;
                }
                var fileName = $"{storedProcedure.Name}.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = GetInputTextForStoredProcedure(schema, storedProcedure);

                output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }

    public void GenerateDataContextOutputs(bool isDryRun)
    {
        var schemas = configFile.Config.Schema
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures;

            if (!storedProcedures.Any())
            {
                continue;
            }

            var dataContextOutputsPath = DirectoryUtils.GetWorkingDirectory(configFile.Config.Project.Output.DataContext.Path, configFile.Config.Project.Output.DataContext.Outputs.Path);
            var path = Path.Combine(dataContextOutputsPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var storedProcedure in storedProcedures)
            {
                if (!storedProcedure.HasOutputs() || storedProcedure.IsDefaultOutput())
                {
                    continue;
                }
                var fileName = $"{storedProcedure.Name}.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = GetOutputTextForStoredProcedure(schema, storedProcedure);

                output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }

    public void GenerateDataContextModels(bool isDryRun)
    {
        var schemas = configFile.Config.Schema
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures
                .Where(i => i.ReadWriteKind == Definition.ReadWriteKindEnum.Read).ToList();

            if (!(storedProcedures.Count != 0))
            {
                continue;
            }

            var dataContextModelPath = DirectoryUtils.GetWorkingDirectory(configFile.Config.Project.Output.DataContext.Path, configFile.Config.Project.Output.DataContext.Models.Path);
            var path = Path.Combine(dataContextModelPath, schema.Path);
            if (!Directory.Exists(path) && !isDryRun)
            {
                Directory.CreateDirectory(path);
            }

            foreach (var storedProcedure in storedProcedures)
            {
                var isScalar = storedProcedure.Output?.Count() == 1;
                if (isScalar)
                {
                    continue;
                }
                var fileName = $"{storedProcedure.Name}.cs";
                var fileNameWithPath = Path.Combine(path, fileName);
                var sourceText = GetModelTextForStoredProcedure(schema, storedProcedure);

                output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }

    public static string GetIdentifierFromSqlInputTableType(string name)
    {
        name = $"{name[1..].FirstCharToLower()}";
        var reservedKeyWords = new[] { "params", "namespace" };
        if (reservedKeyWords.Contains(name))
        {
            name = $"@{name}";
        }
        return name;
    }

    public static string GetPropertyFromSqlInputTableType(string name)
    {
        name = $"{name[1..].FirstCharToUpper()}";
        return name;
    }

    public SourceText GetStoredProcedureText(Definition.Schema schema, List<Definition.StoredProcedure> storedProcedures)
    {
        var entityName = storedProcedures.First().EntityName;

        var rootDir = output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, "DataContext", "StoredProcedures", "StoredProcedureExtensions.cs"));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // If its an extension, add usings for the lib
        if (configFile.Config.Project.Role.Kind == ERoleKind.Extension)
        {
            var libUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Role.LibNamespace}"));
            root = root.AddUsings(libUsingDirective).NormalizeWhitespace();

            var libModelUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Role.LibNamespace}.Models"));
            root = root.AddUsings(libModelUsingDirective).NormalizeWhitespace();

            var libOutputsUsingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Role.LibNamespace}.Outputs"));
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
                    var newUsingName = configFile.Config.Project.Role.Kind == ERoleKind.Lib
                        ? SyntaxFactory.ParseName($"{usingDirective.Name.ToString().Replace("Source.DataContext", configFile.Config.Project.Output.Namespace)}")
                        : SyntaxFactory.ParseName($"{usingDirective.Name.ToString().Replace("Source", configFile.Config.Project.Output.Namespace)}");
                    root = root.ReplaceNode(usingDirective, usingDirective.WithName(newUsingName));
                }
            }
        }

        // Add Using for Models
        // TODO: i.Output?.Count() -> Implement a Property "IsScalar" and "IsJson"
        if (storedProcedures.Any(i => i.ReadWriteKind == Definition.ReadWriteKindEnum.Read && i.Output?.Count() > 1))
        {
            var modelUsingDirective = configFile.Config.Project.Role.Kind == ERoleKind.Lib
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.Models.{schema.Name}"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.DataContext.Models.{schema.Name}"));
            root = root.AddUsings(modelUsingDirective).NormalizeWhitespace();
        }

        // Add Usings for Inputs
        if (storedProcedures.Any(s => s.HasInputs()))
        {
            var inputUsingDirective = configFile.Config.Project.Role.Kind == ERoleKind.Lib
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.Inputs.{schema.Name}"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.DataContext.Inputs.{schema.Name}"));
            root = root.AddUsings(inputUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        // Add Usings for Outputs
        if (storedProcedures.Any(s => s.HasOutputs() && !s.IsDefaultOutput()))
        {
            var inputUsingDirective = configFile.Config.Project.Role.Kind == ERoleKind.Lib
                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.Outputs.{schema.Name}"))
                : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.DataContext.Outputs.{schema.Name}"));
            root = root.AddUsings(inputUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        // Add Usings for TableTypes
        // If Inputs contains a TableType, add using for TableTypes
        var tableTypeSchemas = storedProcedures.SelectMany(sp => sp.Input.Where(i => i.IsTableType ?? false))
                             .GroupBy(t => t.TableTypeSchemaName, (key, group) => key).ToList();

        foreach (var tableTypeSchema in tableTypeSchemas)
        {
            var tableTypeSchemaConfig = configFile.Config.Schema.Find(s => s.Name.Equals(tableTypeSchema));
            var useFromLib = tableTypeSchemaConfig?.Status != SchemaStatusEnum.Build
                && configFile.Config.Project.Role.Kind == ERoleKind.Extension;

            var paramUsingDirective = useFromLib
                                ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Role.LibNamespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                                : configFile.Config.Project.Role.Kind == ERoleKind.Lib
                                    ? SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.TableTypes.{tableTypeSchema.FirstCharToUpper()}"))
                                    : SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{configFile.Config.Project.Output.Namespace}.DataContext.TableTypes.{tableTypeSchema.FirstCharToUpper()}"));
            root = root.AddUsings(paramUsingDirective.NormalizeWhitespace().WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        // Remove Template Usings
        var usings = root.Usings.Where(_ => !_.Name.ToString().StartsWith("Source."));
        root = root.WithUsings([.. usings]);

        // Replace Namespace
        if (configFile.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", configFile.Config.Project.Output.Namespace).Replace("Schema", schema.Name));
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
        var schemas = configFile.Config.Schema
            .Where(i => i.Status == SchemaStatusEnum.Build && (i.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema);

        foreach (var schema in schemas)
        {
            var storedProcedures = schema.StoredProcedures;

            if (!storedProcedures.Any())
            {
                continue;
            }

            var dataContextStoredProcedurePath = DirectoryUtils.GetWorkingDirectory(configFile.Config.Project.Output.DataContext.Path, configFile.Config.Project.Output.DataContext.StoredProcedures.Path);
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

                output.Write(fileNameWithPath, sourceText, isDryRun);
            }
        }
    }
}
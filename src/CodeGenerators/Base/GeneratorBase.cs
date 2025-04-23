using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SpocR.Contracts;
using SpocR.DataContext;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.CodeGenerators.Base;

public abstract class GeneratorBase
{
    protected readonly FileManager<ConfigurationModel> ConfigFile;
    protected readonly OutputService Output;
    protected readonly IReportService ReportService;

    protected GeneratorBase(
        FileManager<ConfigurationModel> configFile,
        OutputService output,
        IReportService reportService)
    {
        ConfigFile = configFile;
        Output = output;
        ReportService = reportService;
    }

    protected TypeSyntax ParseTypeFromSqlDbTypeName(string sqlTypeName, bool isNullable)
    {
        // temporary for #56: we should not abort execution if config is corrupt
        if (string.IsNullOrEmpty(sqlTypeName))
        {
            ReportService.PrintCorruptConfigMessage($"Could not parse 'SqlTypeName' - setting the type to dynamic");
            sqlTypeName = "Variant";
        }

        sqlTypeName = sqlTypeName.Split('(')[0];
        var sqlType = Enum.Parse<System.Data.SqlDbType>(sqlTypeName, true);
        var clrType = SqlDbHelper.GetType(sqlType, isNullable);
        return SyntaxFactory.ParseTypeName(clrType.ToGenericTypeString());
    }

    protected CompilationUnitSyntax ProcessTemplate(string templatePath, string namespaceSuffix, string className)
    {
        var rootDir = Output.GetOutputRootDir();
        var fileContent = File.ReadAllText(Path.Combine(rootDir.FullName, templatePath));

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        // Replace Namespace
        if (ConfigFile.Config.Project.Role.Kind == ERoleKind.Lib)
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", ConfigFile.Config.Project.Output.Namespace).Replace("Schema", namespaceSuffix));
        }
        else
        {
            root = root.ReplaceNamespace(ns => ns.Replace("Source", ConfigFile.Config.Project.Output.Namespace).Replace("Schema", namespaceSuffix));
        }

        // Replace ClassName
        root = root.ReplaceClassName(ci => ci.Replace(Path.GetFileNameWithoutExtension(templatePath), className));

        return root;
    }

    protected static string GetIdentifierFromSqlInputTableType(string name)
    {
        name = $"{name[1..].FirstCharToLower()}";
        var reservedKeyWords = new[] { "params", "namespace" };
        if (reservedKeyWords.Contains(name))
        {
            name = $"@{name}";
        }
        return name;
    }

    protected static string GetPropertyFromSqlInputTableType(string name)
    {
        name = $"{name[1..].FirstCharToUpper()}";
        return name;
    }

    protected static string GetTypeNameForTableType(Definition.TableType tableType)
    {
        return $"{tableType.Name}";
    }

    protected static TypeSyntax GetTypeSyntaxForTableType(StoredProcedureInputModel input)
    {
        return input.Name.EndsWith("List")
            ? SyntaxFactory.ParseTypeName($"IEnumerable<{input.TableTypeName}>")
            : SyntaxFactory.ParseTypeName($"{input.TableTypeName}");
    }
}

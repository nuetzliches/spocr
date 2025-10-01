using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace SpocR.TestFramework.Validation;

/// <summary>
/// Validates generated C# code for syntax, compilation, and best practices
/// </summary>
public class GeneratedCodeValidator
{
    private readonly ILogger<GeneratedCodeValidator> _logger;

    public GeneratedCodeValidator(ILogger<GeneratedCodeValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates all generated C# files in the specified directory
    /// </summary>
    public async Task<ValidationResult> ValidateGeneratedCodeAsync(string outputDirectory)
    {
        var result = new ValidationResult();
        
        if (!Directory.Exists(outputDirectory))
        {
            result.AddError($"Output directory does not exist: {outputDirectory}");
            return result;
        }

        var csharpFiles = Directory.GetFiles(outputDirectory, "*.cs", SearchOption.AllDirectories);
        
        if (csharpFiles.Length == 0)
        {
            result.AddWarning("No C# files found in output directory");
            return result;
        }

        _logger.LogInformation($"Validating {csharpFiles.Length} generated C# files...");

        foreach (var filePath in csharpFiles)
        {
            var fileResult = await ValidateFileAsync(filePath);
            result.Merge(fileResult);
        }

        return result;
    }

    /// <summary>
    /// Validates a single C# file
    /// </summary>
    public async Task<ValidationResult> ValidateFileAsync(string filePath)
    {
        var result = new ValidationResult { FilePath = filePath };

        try
        {
            var sourceCode = await File.ReadAllTextAsync(filePath);
            
            // Parse syntax tree
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var diagnostics = syntaxTree.GetDiagnostics();

            // Check for syntax errors
            foreach (var diagnostic in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                result.AddError($"Syntax error: {diagnostic.GetMessage()} at {diagnostic.Location}");
            }

            // Check for syntax warnings
            foreach (var diagnostic in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning))
            {
                result.AddWarning($"Syntax warning: {diagnostic.GetMessage()} at {diagnostic.Location}");
            }

            // If syntax is valid, perform compilation check
            if (!result.HasErrors)
            {
                await ValidateCompilationAsync(syntaxTree, result);
            }

            // Perform code quality checks
            await ValidateCodeQualityAsync(syntaxTree, result);
        }
        catch (Exception ex)
        {
            result.AddError($"Failed to validate file: {ex.Message}");
        }

        return result;
    }

    private async Task ValidateCompilationAsync(SyntaxTree syntaxTree, ValidationResult result)
    {
        try
        {
            // Create compilation with basic references
            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location)
            };

            var compilation = CSharpCompilation.Create(
                "ValidationCompilation",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compilationDiagnostics = compilation.GetDiagnostics();

            foreach (var diagnostic in compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                result.AddError($"Compilation error: {diagnostic.GetMessage()} at {diagnostic.Location}");
            }

            foreach (var diagnostic in compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning))
            {
                result.AddWarning($"Compilation warning: {diagnostic.GetMessage()} at {diagnostic.Location}");
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.AddError($"Compilation validation failed: {ex.Message}");
        }
    }

    private async Task ValidateCodeQualityAsync(SyntaxTree syntaxTree, ValidationResult result)
    {
        try
        {
            var root = syntaxTree.GetRoot();
            
            // Check for proper using statements
            var usings = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();
            if (!usings.Any())
            {
                result.AddWarning("No using statements found - consider adding necessary imports");
            }

            // Check for proper namespace declarations
            var namespaces = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax>();
            if (!namespaces.Any())
            {
                result.AddWarning("No namespace declarations found");
            }

            // Check for public classes without proper documentation
            var classes = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>();
            foreach (var classDecl in classes.Where(c => c.Modifiers.Any(m => m.ValueText == "public")))
            {
                if (!HasDocumentationComment(classDecl))
                {
                    result.AddInfo($"Public class '{classDecl.Identifier.ValueText}' missing XML documentation");
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.AddError($"Code quality validation failed: {ex.Message}");
        }
    }

    private bool HasDocumentationComment(Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax member)
    {
        return member.HasLeadingTrivia &&
               member.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                                 t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
    }
}

/// <summary>
/// Represents the result of code validation
/// </summary>
public class ValidationResult
{
    public string? FilePath { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> InfoMessages { get; } = new();

    public bool HasErrors => Errors.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
    public bool IsValid => !HasErrors;

    public void AddError(string message) => Errors.Add(message);
    public void AddWarning(string message) => Warnings.Add(message);
    public void AddInfo(string message) => InfoMessages.Add(message);

    public void Merge(ValidationResult other)
    {
        Errors.AddRange(other.Errors);
        Warnings.AddRange(other.Warnings);
        InfoMessages.AddRange(other.InfoMessages);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        
        if (HasErrors)
            parts.Add($"{Errors.Count} error(s)");
        if (HasWarnings)
            parts.Add($"{Warnings.Count} warning(s)");
        if (InfoMessages.Count > 0)
            parts.Add($"{InfoMessages.Count} info message(s)");

        return parts.Count > 0 ? string.Join(", ", parts) : "Valid";
    }
}
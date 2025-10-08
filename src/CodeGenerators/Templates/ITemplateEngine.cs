using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SpocR.Enums;

namespace SpocR.CodeGenerators.Templates;

/// <summary>
/// Interface for the embedded template engine that processes templates with placeholders
/// </summary>
public interface ITemplateEngine
{
    /// <summary>
    /// Gets a processed template with all placeholders replaced
    /// </summary>
    /// <param name="templateType">The type of template (e.g., "AppDbContext", "StoredProcedure")</param>
    /// <param name="targetFramework">Target .NET framework version</param>
    /// <param name="placeholders">Dictionary of placeholder values to replace</param>
    /// <returns>Processed compilation unit ready for code generation</returns>
    Task<CompilationUnitSyntax> GetProcessedTemplateAsync(
        TemplateType templateType, 
        TargetFrameworkEnum targetFramework, 
        Dictionary<string, object> placeholders);

    /// <summary>
    /// Checks if a template exists for the given type and framework
    /// </summary>
    /// <param name="templateType">Template type to check</param>
    /// <param name="targetFramework">Target framework</param>
    /// <returns>True if template exists, false otherwise</returns>
    bool TemplateExists(TemplateType templateType, TargetFrameworkEnum targetFramework);

    /// <summary>
    /// Gets available template types
    /// </summary>
    /// <returns>Collection of available template types</returns>
    IEnumerable<TemplateType> GetAvailableTemplateTypes();
}

/// <summary>
/// Template types supported by the engine
/// </summary>
public enum TemplateType
{
    AppDbContext,
    ModernAppDbContext,
    AppDbContextExtensions,
    ServiceCollectionExtensions,
    SqlDataReaderExtensions,
    SqlParameterExtensions,
    StoredProcedureExtensions,
    MinimalApiExtensions,
    InputModel,
    OutputModel,
    EntityModel,
    TableType
}
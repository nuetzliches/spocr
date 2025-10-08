using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SpocR.Enums;
using SpocR.Extensions;

namespace SpocR.CodeGenerators.Templates;

/// <summary>
/// Embedded template engine that loads templates from embedded resources and processes placeholders
/// </summary>
public class EmbeddedTemplateEngine : ITemplateEngine
{
    private readonly Dictionary<string, string> _templateCache = new();
    private readonly Dictionary<string, CompilationUnitSyntax> _compiledTemplateCache = new();
    
    private static readonly Assembly CurrentAssembly = Assembly.GetExecutingAssembly();
    private const string TemplateResourcePrefix = "SpocR.Templates.";

    public EmbeddedTemplateEngine()
    {
        LoadEmbeddedTemplates();
    }

    public async Task<CompilationUnitSyntax> GetProcessedTemplateAsync(
        TemplateType templateType, 
        TargetFrameworkEnum targetFramework, 
        Dictionary<string, object> placeholders)
    {
        var templateKey = GetTemplateKey(templateType, targetFramework);
        
        // Try to get the specific framework template first
        if (!_templateCache.TryGetValue(templateKey, out var templateContent))
        {
            // Fallback to default template
            var defaultKey = GetTemplateKey(templateType, Constants.DefaultTargetFramework);
            if (!_templateCache.TryGetValue(defaultKey, out templateContent))
            {
                throw new InvalidOperationException($"Template not found: {templateType} for {targetFramework}");
            }
        }

        // Process placeholders
        var processedContent = await ProcessPlaceholdersAsync(templateContent, placeholders ?? new Dictionary<string, object>());
        
        // Parse and return
        var syntaxTree = CSharpSyntaxTree.ParseText(processedContent);
        return syntaxTree.GetCompilationUnitRoot();
    }

    public bool TemplateExists(TemplateType templateType, TargetFrameworkEnum targetFramework)
    {
        var templateKey = GetTemplateKey(templateType, targetFramework);
        return _templateCache.ContainsKey(templateKey) || 
               _templateCache.ContainsKey(GetTemplateKey(templateType, Constants.DefaultTargetFramework));
    }

    public IEnumerable<TemplateType> GetAvailableTemplateTypes()
    {
        return _templateCache.Keys
            .Select(key => key.Split('.')[0])
            .Distinct()
            .Where(name => Enum.TryParse<TemplateType>(name, out _))
            .Select(name => Enum.Parse<TemplateType>(name));
    }

    private void LoadEmbeddedTemplates()
    {
        var resourceNames = CurrentAssembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(TemplateResourcePrefix) && name.EndsWith(".cs.template"))
            .ToList();

        foreach (var resourceName in resourceNames)
        {
            using var stream = CurrentAssembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = reader.ReadToEnd();
            
            // Extract template type and framework from resource name
            // Format: SpocR.Templates.{TemplateType}.{Framework}.cs.template
            var parts = resourceName.Replace(TemplateResourcePrefix, "").Replace(".cs.template", "").Split('.');
            
            if (parts.Length >= 1)
            {
                var templateType = parts[0];
                var framework = parts.Length > 1 ? parts[1] : "Default";
                var key = $"{templateType}.{framework}";
                
                _templateCache[key] = content;
            }
        }
    }

    private async Task<string> ProcessPlaceholdersAsync(string templateContent, Dictionary<string, object> placeholders)
    {
        var result = templateContent;

        foreach (var placeholder in placeholders)
        {
            var placeholderKey = $"{{{{{placeholder.Key}}}}}"; // Format: {{PlaceholderName}}
            var value = placeholder.Value?.ToString() ?? string.Empty;
            
            result = result.Replace(placeholderKey, value);
        }

        // Process conditional placeholders (e.g., {{#if HasTransactions}}...{{/if}})
        result = await ProcessConditionalPlaceholdersAsync(result, placeholders);
        
        // Process loop placeholders (e.g., {{#each Properties}}...{{/each}})
        result = await ProcessLoopPlaceholdersAsync(result, placeholders);

        return result;
    }

    private Task<string> ProcessConditionalPlaceholdersAsync(string content, Dictionary<string, object> placeholders)
    {
        // Simple conditional processing
        // Format: {{#if ConditionName}}content{{/if}}
        var result = content;
        var startPattern = "{{#if ";
        var endPattern = "{{/if}}";

        while (true)
        {
            var startIndex = result.IndexOf(startPattern, StringComparison.Ordinal);
            if (startIndex == -1) break;

            var conditionStart = startIndex + startPattern.Length;
            var conditionEnd = result.IndexOf("}}", conditionStart, StringComparison.Ordinal);
            if (conditionEnd == -1) break;

            var condition = result.Substring(conditionStart, conditionEnd - conditionStart);
            var contentStart = conditionEnd + 2;
            var contentEnd = result.IndexOf(endPattern, contentStart, StringComparison.Ordinal);
            if (contentEnd == -1) break;

            var conditionalContent = result.Substring(contentStart, contentEnd - contentStart);
            var replacement = "";

            // Check if condition is true
            if (placeholders.TryGetValue(condition, out var value))
            {
                if (value is bool boolValue && boolValue)
                {
                    replacement = conditionalContent;
                }
                else if (value != null && !string.IsNullOrEmpty(value.ToString()))
                {
                    replacement = conditionalContent;
                }
            }

            result = result.Substring(0, startIndex) + replacement + result.Substring(contentEnd + endPattern.Length);
        }

        return Task.FromResult(result);
    }

    private Task<string> ProcessLoopPlaceholdersAsync(string content, Dictionary<string, object> placeholders)
    {
        // Simple loop processing
        // Format: {{#each CollectionName}}{{PropertyName}}{{/each}}
        var result = content;
        var startPattern = "{{#each ";
        var endPattern = "{{/each}}";

        while (true)
        {
            var startIndex = result.IndexOf(startPattern, StringComparison.Ordinal);
            if (startIndex == -1) break;

            var collectionStart = startIndex + startPattern.Length;
            var collectionEnd = result.IndexOf("}}", collectionStart, StringComparison.Ordinal);
            if (collectionEnd == -1) break;

            var collectionName = result.Substring(collectionStart, collectionEnd - collectionStart);
            var contentStart = collectionEnd + 2;
            var contentEnd = result.IndexOf(endPattern, contentStart, StringComparison.Ordinal);
            if (contentEnd == -1) break;

            var loopTemplate = result.Substring(contentStart, contentEnd - contentStart);
            var replacement = "";

            // Process collection
            if (placeholders.TryGetValue(collectionName, out var collection) && collection is System.Collections.IEnumerable enumerable)
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                {
                    var itemContent = loopTemplate;
                    if (item is Dictionary<string, object> itemPlaceholders)
                    {
                        foreach (var itemPlaceholder in itemPlaceholders)
                        {
                            var itemKey = $"{{{{{itemPlaceholder.Key}}}}}";
                            var itemValue = itemPlaceholder.Value?.ToString() ?? string.Empty;
                            itemContent = itemContent.Replace(itemKey, itemValue);
                        }
                    }
                    items.Add(itemContent);
                }
                replacement = string.Join(Environment.NewLine, items);
            }

            result = result.Substring(0, startIndex) + replacement + result.Substring(contentEnd + endPattern.Length);
        }

        return Task.FromResult(result);
    }

    private static string GetTemplateKey(TemplateType templateType, TargetFrameworkEnum targetFramework)
    {
        return $"{templateType}.{targetFramework}";
    }
}
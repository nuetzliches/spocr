using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpocR.SpocRVNext.Engine;
/// <summary>
/// Very small placeholder engine: {{ PropertyName }} is replaced by its value.
/// Not meant for complex logic; intentionally minimal.
/// </summary>
public sealed class SimpleTemplateEngine : ITemplateRenderer
{
    // Regex: {{ Name }} oder {{  complex.path_value  }}
    private static readonly Regex Placeholder = new(@"\{\{\s*(?<name>[A-Za-z0-9_\.]+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex EachBlock = new(@"\{\{#each\s+(?<path>[A-Za-z0-9_\.]+)\s*\}\}(?<body>[\s\S]*?)\{\{/each\}\}", RegexOptions.Compiled);
    private static readonly Regex IfBlock = new(@"\{\{#if\s+(?<expr>[A-Za-z0-9_\.]+)\s*\}\}(?<body>[\s\S]*?)(\{\{else\}\}(?<else>[\s\S]*?))?\{\{/if\}\}", RegexOptions.Compiled);

    public string Render(string template, object? model)
    {
        if (template == null) throw new ArgumentNullException(nameof(template));
        if (string.IsNullOrEmpty(template) || model == null)
            return template; // nothing to replace

        // Serialize model to JsonElement for generic traversal
        var json = JsonSerializer.SerializeToElement(model, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        // Process if blocks first (so nested #each inside truthy branches becomes visible)
        template = IfBlock.Replace(template, m =>
        {
            var expr = m.Groups["expr"].Value.Split('.');
            var body = m.Groups["body"].Value;
            var elseBody = m.Groups["else"].Success ? m.Groups["else"].Value : string.Empty;
            if (TryResolve(json, expr, out var flag))
            {
                bool truthy = flag.ValueKind switch
                {
                    JsonValueKind.False => false,
                    JsonValueKind.Null => false,
                    JsonValueKind.Undefined => false,
                    JsonValueKind.Number => true,
                    JsonValueKind.String => !string.IsNullOrEmpty(flag.ToString()),
                    JsonValueKind.Array => flag.GetArrayLength() > 0,
                    JsonValueKind.Object => true,
                    JsonValueKind.True => true,
                    _ => false
                };
                return truthy ? body : elseBody;
            }
            return elseBody; // unresolved treated as false
        });

        // Then process each blocks (which might have been revealed by #if)
        template = EachBlock.Replace(template, m =>
        {
            var pathSegs = m.Groups["path"].Value.Split('.');
            if (!TryResolve(json, pathSegs, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return string.Empty;
            var body = m.Groups["body"].Value;
            var sb = new System.Text.StringBuilder();
            foreach (var element in arr.EnumerateArray())
            {
                // For body rendering allow {{this.Property}} or {{Property}}
                sb.Append(Placeholder.Replace(body, pm =>
                {
                    var name = pm.Groups["name"].Value;
                    JsonElement resolved;
                    if (name.Equals("this", StringComparison.OrdinalIgnoreCase)) return element.ToString() ?? string.Empty;
                    var parts = name.StartsWith("this.", StringComparison.OrdinalIgnoreCase)
                        ? name.Substring(5).Split('.')
                        : name.Split('.');
                    if (TryResolve(element, parts, out resolved) && resolved.ValueKind != JsonValueKind.Undefined && resolved.ValueKind != JsonValueKind.Null)
                        return resolved.ToString() ?? string.Empty;
                    // Fallback: try root json (outer scope) for placeholder not found in element
                    if (TryResolve(json, name.Split('.'), out resolved) && resolved.ValueKind != JsonValueKind.Undefined && resolved.ValueKind != JsonValueKind.Null)
                        return resolved.ToString() ?? string.Empty;
                    return string.Empty;
                }));
            }
            return sb.ToString();
        });


        return Placeholder.Replace(template, m =>
        {
            var path = m.Groups["name"].Value.Split('.');
            if (TryResolve(json, path, out var value) && value.ValueKind != JsonValueKind.Undefined && value.ValueKind != JsonValueKind.Null)
            {
                return value.ToString() ?? string.Empty;
            }
            return string.Empty; // missing placeholder -> empty (deterministic)
        });
    }

    private static bool TryResolve(JsonElement current, string[] path, out JsonElement value)
    {
        value = current;
        foreach (var segment in path)
        {
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(segment, out var next))
            {
                value = next;
                continue;
            }
            value = default;
            return false;
        }
        return true;
    }
}
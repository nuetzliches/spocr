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
using System;
using System.Text.Json;

namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// Shared JSON helpers for snapshot metadata to avoid duplicating JsonElement extension methods.
/// </summary>
internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.GetString() : null;

    public static bool GetPropertyOrDefaultBool(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            JsonValueKind.Number => value.TryGetInt32(out var numeric) && numeric != 0,
            _ => false
        };
    }

    public static bool GetPropertyOrDefaultBoolStrict(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            JsonValueKind.Number => value.TryGetInt32(out var numeric) && numeric != 0,
            _ => false
        };
    }

    public static int? GetPropertyOrDefaultInt(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Number) return null;
        return value.TryGetInt32(out var parsed) ? parsed : (int?)null;
    }
}

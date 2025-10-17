using System;
using System.Linq;

namespace SpocR.SpocRVNext.Utils;

/// <summary>
/// Centralized name sanitization helpers to ensure consistent transformation of raw database or snapshot names
/// into safe C# identifiers and filesystem-friendly file names.
/// </summary>
public static class NameSanitizer
{
    /// <summary>
    /// Sanitize a raw name for file usage (snapshot filenames, etc.).
    /// Rules:
    ///  - Preserve letters, digits and separators '.', '_', '-'.
    ///  - Replace any other character with '-'.
    ///  - Collapse consecutive occurrences of the same separator (.., __, --).
    ///  - Trim leading/trailing separators.
    ///  - Fallback to '_' if empty.
    /// </summary>
    public static string SanitizeForFile(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "_";
        var transformed = raw.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray();
        var s = new string(transformed);
        foreach (var sep in new[] {"..", "__", "--"})
            while (s.Contains(sep)) s = s.Replace(sep, sep.Substring(0,1));
        s = s.Trim('.', '_', '-');
        return string.IsNullOrEmpty(s) ? "_" : s;
    }

    /// <summary>
    /// Sanitize a raw name for use as a C# identifier (type or property). More restrictive than file variant.
    /// Rules:
    ///  - Keep letters and digits; convert other characters to '_'.
    ///  - Collapse multiple underscores.
    ///  - If starts with digit, prefix with 'N'.
    ///  - Fallback to 'Identifier' if empty.
    /// </summary>
    public static string SanitizeIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Identifier";
        var chars = raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var s = new string(chars);
        while (s.Contains("__")) s = s.Replace("__", "_");
        s = s.Trim('_');
        if (string.IsNullOrEmpty(s)) s = "Identifier";
        if (char.IsDigit(s[0])) s = "N" + s;
        return s;
    }
}

using System;
using System.Text.RegularExpressions;
using SpocR.Services;

namespace SpocR.Services;

/// <summary>
/// Gemeinsame heuristische Typ-Inferenz für JSON-Extraktionen aus Funktionen und Prozeduren.
/// Ziel: Konsistente Ableitung von SqlTypeName / MaxLength / Nullability.
/// </summary>
public static class SqlTypeInference
{
    public static (string sqlType, int maxLength, bool isNullable) Infer(JsonFunctionAstColumn c)
    {
        if (c == null) return ("nvarchar(max)", -1, true);
        string? src = c.SourceSql;
        // 1) JSON Flags
        if (c.IsNestedJson || c.ReturnsJson) return ("json", -1, true);
        // 2) CAST / CONVERT
        if (!string.IsNullOrWhiteSpace(src))
        {
            var castMatch = Regex.Match(src, @"CAST\s*\(.+?\s+AS\s+(?<t>[A-Za-z0-9_]+(\(max\)|\(\d+\))?)\)", RegexOptions.IgnoreCase);
            if (castMatch.Success)
            {
                var t = castMatch.Groups["t"].Value; return (t, ExtractLen(t), true);
            }
            var convMatch = Regex.Match(src, @"CONVERT\s*\(\s*(?<t>[A-Za-z0-9_]+(\(max\)|\(\d+\))?)\s*,", RegexOptions.IgnoreCase);
            if (convMatch.Success)
            { var t = convMatch.Groups["t"].Value; return (t, ExtractLen(t), true); }
        }
        // 3) Literale
        if (!string.IsNullOrWhiteSpace(src))
        {
            if (Regex.IsMatch(src, @"'[^']*'"))
            {
                var inner = Regex.Match(src, @"'([^']*)'");
                if (inner.Success && inner.Groups[1].Value.Length > 0 && inner.Groups[1].Value.Length <= 4000)
                { int len = inner.Groups[1].Value.Length; return ($"nvarchar({len})", len, true); }
                return ("nvarchar(max)", -1, true);
            }
            if (Regex.IsMatch(src, @"\b\d+\b"))
            { return ("int", 4, true); }
            if (Regex.IsMatch(src, @"\b[0-9]+\.[0-9]+\b"))
            { return ("decimal(18,2)", 0, true); }
            if (Regex.IsMatch(src, @"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}"))
            { return ("uniqueidentifier", 16, true); }
        }
        // 4) Name-Muster
        var name = c.Name ?? string.Empty;
        if (name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) return ("int", 4, true);
        if (name.StartsWith("is", StringComparison.OrdinalIgnoreCase) || name.StartsWith("has", StringComparison.OrdinalIgnoreCase)) return ("bit", 1, true);
        if (name.EndsWith("Date", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Utc", StringComparison.OrdinalIgnoreCase)) return ("datetime2", 8, true);
        if (name.IndexOf("rowVersion", StringComparison.OrdinalIgnoreCase) >= 0) return ("rowversion", 8, true);
        if (name.EndsWith("Code", StringComparison.OrdinalIgnoreCase)) return ("nvarchar(50)", 50, true);
        if (name.EndsWith("Name", StringComparison.OrdinalIgnoreCase)) return ("nvarchar(200)", 200, true);
        if (name.EndsWith("Description", StringComparison.OrdinalIgnoreCase)) return ("nvarchar(1000)", 1000, true);
        // 5) Fallback
        return ("nvarchar(max)", -1, true);
    }

    private static int ExtractLen(string t)
    {
        if (string.IsNullOrWhiteSpace(t)) return 0;
        var m = Regex.Match(t, @"\((?<len>\d+)\)");
        if (m.Success && int.TryParse(m.Groups["len"].Value, out var len)) return len;
        if (t.Contains("(max)", StringComparison.OrdinalIgnoreCase)) return -1;
        return 0;
    }
}

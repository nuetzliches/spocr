using System;
using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// Provides deterministic mapping from normalized SQL Server type declarations to CLR types.
/// Focused on scalar & TVF row column mapping for vNext function snapshot.
/// </summary>
internal static class SqlClrTypeMapper
{
    private static readonly Dictionary<string, string> _baseMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int"] = "int",
        ["bigint"] = "long",
        ["smallint"] = "short",
        ["tinyint"] = "byte",
        ["bit"] = "bool",
        ["float"] = "double",      // SQL float = double precision
        ["real"] = "float",        // SQL real = single precision
        ["money"] = "decimal",
        ["smallmoney"] = "decimal",
        ["uniqueidentifier"] = "Guid",
        ["date"] = "DateTime",
        ["datetime"] = "DateTime",
        ["smalldatetime"] = "DateTime",
        ["datetime2"] = "DateTime",
        ["datetimeoffset"] = "DateTimeOffset",
        ["time"] = "TimeSpan",
        ["xml"] = "string",
        ["image"] = "byte[]",
        ["timestamp"] = "byte[]",
        ["rowversion"] = "byte[]",
        ["hierarchyid"] = "string", // MVP: treat as string; future: specialized type
        ["geography"] = "byte[]",   // placeholder
        ["geometry"] = "byte[]",    // placeholder
        ["sql_variant"] = "object"
    };

    /// <summary>
    /// Maps a raw SQL type name (possibly including length/precision like varchar(50), decimal(18,2)) to a CLR type string.
    /// Nullable suffix '?' is appended for value types if <paramref name="isNullable"/> is true.
    /// </summary>
    public static string Map(string sqlTypeRaw, bool isNullable)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeRaw)) return "string"; // defensive default
        var normalized = Normalize(sqlTypeRaw);
        // Explicit decimals / numerics unify to decimal
        if (normalized.StartsWith("decimal") || normalized.StartsWith("numeric"))
            return AppendNullable("decimal", isNullable);

        // Character / text families -> string
        if (IsStringFamily(normalized)) return "string";

        // Binary families
        if (IsBinaryFamily(normalized)) return "byte[]";

        if (_baseMap.TryGetValue(normalized, out var mapped))
            return AppendNullable(mapped, isNullable);

        // Fallback: treat unknown as string (non-nullable string always allowed)
        return "string";
    }

    private static string AppendNullable(string core, bool nullable)
    {
        if (core == "string" || core == "byte[]" || core == "object") return core; // reference types remain unchanged
        return nullable ? core + "?" : core;
    }

    private static bool IsStringFamily(string n)
    {
        return n.StartsWith("char") || n.StartsWith("nchar") || n.StartsWith("varchar") || n.StartsWith("nvarchar")
               || n.StartsWith("text") || n.StartsWith("ntext") || n.StartsWith("sysname");
    }

    private static bool IsBinaryFamily(string n)
    {
        return n.StartsWith("binary") || n.StartsWith("varbinary") || n.StartsWith("image") || n.StartsWith("timestamp") || n.StartsWith("rowversion");
    }

    /// <summary>
    /// Normalizes a SQL type token by stripping trailing spaces and keeping leading type keyword (e.g. decimal(18,2) -> decimal).
    /// </summary>
    private static string Normalize(string raw)
    {
        raw = raw.Trim();
        int paren = raw.IndexOf('(');
        if (paren > 0) raw = raw.Substring(0, paren);
        // remove extra spaces
        return raw.ToLowerInvariant();
    }
}

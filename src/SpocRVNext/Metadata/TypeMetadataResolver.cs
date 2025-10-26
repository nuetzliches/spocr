using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SpocRVNext.Metadata;

/// <summary>
/// Resolves <c>schema.name</c> type references coming from expanded snapshot artifacts into
/// concrete SQL type descriptors (including base type, formatted type string, precision metadata).
/// The resolver caches scalar user-defined types sourced from <c>.spocr/schema/types</c>.
/// </summary>
internal sealed class TypeMetadataResolver
{
    private readonly Dictionary<string, ScalarTypeInfo> _scalarTypes;

    public TypeMetadataResolver(string? projectRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(projectRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectRoot!);
        _scalarTypes = LoadScalarTypes(root);
    }

    public ResolvedType? Resolve(string? typeRef, int? maxLength, int? precision, int? scale)
    {
        var (schema, name) = SplitTypeRef(typeRef);
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string baseSqlType;
        var effectiveMax = NormalizeLength(maxLength);
        var effectivePrecision = NormalizePrecision(precision);
        var effectiveScale = NormalizePrecision(scale);

        if (string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase))
        {
            baseSqlType = NormalizeBaseType(name);
        }
        else if (_scalarTypes.TryGetValue(BuildKey(schema, name), out var scalar))
        {
            baseSqlType = NormalizeBaseType(scalar.BaseSqlTypeName ?? scalar.Name ?? name);
            effectiveMax ??= NormalizeLength(scalar.MaxLength);
            effectivePrecision ??= NormalizePrecision(scalar.Precision);
            effectiveScale ??= NormalizePrecision(scalar.Scale);
        }
        else
        {
            return null;
        }

        var sqlType = FormatSqlType(baseSqlType, effectiveMax, effectivePrecision, effectiveScale);
        return new ResolvedType(baseSqlType, sqlType, effectiveMax, effectivePrecision, effectiveScale);
    }

    public static (string? Schema, string? Name) SplitTypeRef(string? typeRef)
    {
        if (string.IsNullOrWhiteSpace(typeRef)) return (null, null);
        var trimmed = typeRef.Trim();
        var parts = trimmed.Split('.', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return (parts[0], parts[1]);
        }
        return (null, parts.Length == 1 ? parts[0] : null);
    }

    private static Dictionary<string, ScalarTypeInfo> LoadScalarTypes(string projectRoot)
    {
        var map = new Dictionary<string, ScalarTypeInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var typesDir = Path.Combine(projectRoot, ".spocr", "schema", "types");
            if (!Directory.Exists(typesDir)) return map;
            var files = Directory.GetFiles(typesDir, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                try
                {
                    using var fs = File.OpenRead(file);
                    using var doc = JsonDocument.Parse(fs);
                    var root = doc.RootElement;
                    var schema = root.GetPropertyOrDefault("Schema") ?? "dbo";
                    var name = root.GetPropertyOrDefault("Name") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var info = new ScalarTypeInfo(
                        Schema: schema,
                        Name: name,
                        BaseSqlTypeName: root.GetPropertyOrDefault("BaseSqlTypeName") ?? root.GetPropertyOrDefault("SqlTypeName"),
                        MaxLength: root.GetPropertyOrDefaultInt("MaxLength"),
                        Precision: root.GetPropertyOrDefaultInt("Precision"),
                        Scale: root.GetPropertyOrDefaultInt("Scale")
                    );
                    map[BuildKey(schema, name)] = info;
                }
                catch
                {
                    // ignore individual file parse issues; remaining entries still help resolve types
                }
            }
        }
        catch
        {
            // ignore directory enumeration issues
        }
        return map;
    }

    private static string BuildKey(string schema, string name)
        => string.Concat(schema?.Trim() ?? string.Empty, ".", name?.Trim() ?? string.Empty);

    private static string NormalizeBaseType(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static int? NormalizeLength(int? value)
    {
        if (!value.HasValue) return null;
        var val = value.Value;
        if (val < 0) return -1; // e.g. MAX types
        if (val == 0) return null;
        return val;
    }

    private static int? NormalizePrecision(int? value)
    {
        if (!value.HasValue) return null;
        var val = value.Value;
        return val <= 0 ? null : val;
    }

    private static string FormatSqlType(string baseType, int? maxLength, int? precision, int? scale)
    {
        if (string.IsNullOrWhiteSpace(baseType)) return string.Empty;
        var normalized = baseType.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "decimal":
            case "numeric":
                if (precision.HasValue)
                {
                    var effectiveScale = scale ?? 0;
                    return $"{normalized}({precision.Value},{effectiveScale})";
                }
                return normalized;
            case "varchar":
            case "nvarchar":
            case "varbinary":
            case "char":
            case "nchar":
            case "binary":
                if (maxLength.HasValue)
                {
                    if (maxLength.Value < 0) return $"{normalized}(max)";
                    return $"{normalized}({maxLength.Value})";
                }
                return $"{normalized}(max)";
            case "datetime2":
            case "datetimeoffset":
            case "time":
                if (scale.HasValue)
                {
                    return $"{normalized}({scale.Value})";
                }
                return normalized;
            default:
                return normalized;
        }
    }
}

internal sealed record ScalarTypeInfo(
    string Schema,
    string Name,
    string? BaseSqlTypeName,
    int? MaxLength,
    int? Precision,
    int? Scale
);

internal readonly record struct ResolvedType(
    string BaseSqlType,
    string SqlType,
    int? MaxLength,
    int? Precision,
    int? Scale
);

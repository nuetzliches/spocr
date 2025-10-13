using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpocRVNext.Metadata;

/// <summary>
/// Minimal vNext-only reader for user defined table type metadata sourced from the latest snapshot under .spocr/schema.
/// Avoids legacy runtime model dependencies; produces a lightweight immutable model collection for code generation.
/// </summary>
public interface ITableTypeMetadataProvider
{
    IReadOnlyList<TableTypeInfo> GetAll();
}

public sealed class TableTypeInfo
{
    public string Schema { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = Array.Empty<ColumnInfo>();
}

public sealed class ColumnInfo
{
    public string Name { get; init; } = string.Empty;
    public string SqlType { get; init; } = string.Empty;
    public bool IsNullable { get; init; }
    public int? MaxLength { get; init; }
}

internal sealed class TableTypeMetadataProvider : ITableTypeMetadataProvider
{
    private IReadOnlyList<TableTypeInfo>? _cache;
    private readonly string _projectRoot;

    public TableTypeMetadataProvider(string? projectRoot = null)
    {
        _projectRoot = string.IsNullOrWhiteSpace(projectRoot) ? Directory.GetCurrentDirectory() : Path.GetFullPath(projectRoot!);
    }

    public IReadOnlyList<TableTypeInfo> GetAll()
    {
        if (_cache != null) return _cache;
    var schemaDir = Path.Combine(_projectRoot, ".spocr", "schema");
        if (!Directory.Exists(schemaDir)) return _cache = Array.Empty<TableTypeInfo>();
        var files = Directory.GetFiles(schemaDir, "*.json");
        if (files.Length == 0) return _cache = Array.Empty<TableTypeInfo>();
        var latest = files.Select(f => new FileInfo(f)).OrderByDescending(fi => fi.LastWriteTimeUtc).First();
        using var fs = File.OpenRead(latest.FullName);
        using var doc = JsonDocument.Parse(fs);
        if (!doc.RootElement.TryGetProperty("UserDefinedTableTypes", out var udtts) || udtts.ValueKind != JsonValueKind.Array)
        {
            return _cache = Array.Empty<TableTypeInfo>();
        }
        var list = new List<TableTypeInfo>();
        foreach (var tt in udtts.EnumerateArray())
        {
            var schema = tt.GetPropertyOrDefault("Schema") ?? "dbo";
            var name = tt.GetPropertyOrDefault("Name") ?? "";
            var cols = new List<ColumnInfo>();
            if (tt.TryGetProperty("Columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in colsEl.EnumerateArray())
                {
                    cols.Add(new ColumnInfo
                    {
                        Name = c.GetPropertyOrDefault("Name") ?? string.Empty,
                        SqlType = c.GetPropertyOrDefault("SqlTypeName") ?? string.Empty,
                        IsNullable = c.GetPropertyOrDefaultBool("IsNullable"),
                        MaxLength = c.GetPropertyOrDefaultInt("MaxLength")
                    });
                }
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                list.Add(new TableTypeInfo
                {
                    Schema = schema,
                    Name = name,
                    Columns = cols
                });
            }
        }
        _cache = list.OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
        return _cache;
    }
}

internal static class JsonExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement el, string name)
        => el.TryGetProperty(name, out var v) ? v.GetString() : null;
    public static bool GetPropertyOrDefaultBool(this JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.True && v.ValueKind == JsonValueKind.False ? false : (el.TryGetProperty(name, out var v2) && v2.GetBoolean()));
    public static int? GetPropertyOrDefaultInt(this JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpocRVNext.Metadata; // ColumnInfo reuse

namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// Liest Tabellen-Metadaten aus dem erweiterten Snapshot (.spocr/schema/tables).
/// Schlanke, reine Lese-Schicht (keine Heuristiken, keine Fallbacks auf alte Monolith-Dateien).
/// </summary>
internal interface ITableMetadataProvider
{
    IReadOnlyList<TableInfo> GetAll();
    TableInfo? TryGet(string schema, string name);
}

internal sealed class TableInfo
{
    public string Schema { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = Array.Empty<ColumnInfo>();
}

// Reuse ColumnInfo aus TableTypeMetadataProvider falls vorhanden; falls Namespace-Konflikt existiert, definieren wir kompatiblen Typ.
// Der bestehende ColumnInfo befindet sich in SpocRVNext.Metadata (gleicher Namespace) -> wir können ihn direkt verwenden.
// Falls Build-Fehler auftritt (nicht gefunden), definieren wir minimalen Typ.
// Hier Zusatz-Schutz: falls bereits definiert, wird diese partielle Definition ignoriert.
#if false
internal sealed class ColumnInfo
{
    public string Name { get; init; } = string.Empty;
    public string SqlType { get; init; } = string.Empty;
    public bool IsNullable { get; init; }
    public int? MaxLength { get; init; }
}
#endif
internal sealed class TableMetadataProvider : ITableMetadataProvider
{
    private readonly string _projectRoot;
    private IReadOnlyList<TableInfo>? _cache;
    private Dictionary<string, TableInfo>? _map;

    public TableMetadataProvider(string? projectRoot = null)
    {
        _projectRoot = string.IsNullOrWhiteSpace(projectRoot) ? Directory.GetCurrentDirectory() : Path.GetFullPath(projectRoot!);
    }

    public IReadOnlyList<TableInfo> GetAll()
    {
        if (_cache != null) return _cache;
        var schemaDir = Path.Combine(_projectRoot, ".spocr", "schema", "tables");
        if (!Directory.Exists(schemaDir)) return _cache = Array.Empty<TableInfo>();
        var files = Directory.GetFiles(schemaDir, "*.json");
        var list = new List<TableInfo>(files.Length);
        foreach (var f in files)
        {
            try
            {
                using var fs = File.OpenRead(f);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;
                var schema = root.GetPropertyOrDefault("Schema") ?? root.GetPropertyOrDefault("SchemaName") ?? "dbo";
                var name = root.GetPropertyOrDefault("Name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var cols = new List<ColumnInfo>();
                if (root.TryGetProperty("Columns", out var colsEl) && colsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var c in colsEl.EnumerateArray())
                    {
                        var baseType = (c.GetPropertyOrDefault("SqlTypeName") ?? c.GetPropertyOrDefault("SqlType") ?? string.Empty).Trim();
                        var baseLower = baseType.ToLowerInvariant();
                        var maxLen = c.GetPropertyOrDefaultInt("MaxLength");
                        var prec = c.GetPropertyOrDefaultInt("Precision");
                        var scale = c.GetPropertyOrDefaultInt("Scale");
                        string typeString = baseType;
                        try
                        {
                            if (baseLower == "decimal" || baseLower == "numeric")
                            {
                                if (prec.HasValue && scale.HasValue) typeString = $"{baseLower}({prec.Value},{scale.Value})";
                            }
                            else if (baseLower == "varchar" || baseLower == "nvarchar" || baseLower == "varbinary" || baseLower == "char" || baseLower == "nchar")
                            {
                                if (maxLen.HasValue && maxLen.Value > 0) typeString = $"{baseLower}({maxLen.Value})";
                            }
                        }
                        catch { }
                        cols.Add(new ColumnInfo
                        {
                            Name = c.GetPropertyOrDefault("Name") ?? string.Empty,
                            SqlType = typeString,
                            IsNullable = SpocR.SpocRVNext.Metadata.TableMetadataProviderJsonExtensions.GetPropertyOrDefaultBoolStrict(c, "IsNullable"),
                            MaxLength = maxLen
                        });
                    }
                }
                list.Add(new TableInfo { Schema = schema, Name = name, Columns = cols });
            }
            catch { /* ignore single file */ }
        }
        _cache = list.OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
        _map = _cache.ToDictionary(t => t.Schema + "." + t.Name, StringComparer.OrdinalIgnoreCase);
        return _cache;
    }

    public TableInfo? TryGet(string schema, string name)
    {
        if (_map == null) GetAll();
        var key = schema + "." + name;
        return _map != null && _map.TryGetValue(key, out var ti) ? ti : null;
    }
}

internal static class TableMetadataProviderJsonExtensions
{
    public static string? GetPropertyOrDefault(this System.Text.Json.JsonElement el, string name)
        => el.TryGetProperty(name, out var v) ? v.GetString() : null;
    public static bool GetPropertyOrDefaultBoolStrict(this System.Text.Json.JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == System.Text.Json.JsonValueKind.True) return true;
        if (v.ValueKind == System.Text.Json.JsonValueKind.False) return false;
        if (v.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var s = v.GetString();
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return false;
        }
        if (v.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            if (v.TryGetInt32(out var i)) return i != 0;
            return false;
        }
        return false;
    }
    public static int? GetPropertyOrDefaultInt(this System.Text.Json.JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}

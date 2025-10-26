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

        // Strategy:
        // 1. Prefer expanded snapshot index.json if it contains UserDefinedTableTypes.
        // 2. Fallback to latest monolith json containing UserDefinedTableTypes.
        // 3. If none found -> empty.
        JsonElement udtts = default;
        bool found = false;
        var indexPath = Path.Combine(schemaDir, "index.json");
        if (File.Exists(indexPath))
        {
            try
            {
                using var ifs = File.OpenRead(indexPath);
                using var idoc = JsonDocument.Parse(ifs);
                if (idoc.RootElement.TryGetProperty("UserDefinedTableTypes", out var idxUdtts) && idxUdtts.ValueKind == JsonValueKind.Array)
                {
                    udtts = idxUdtts;
                    found = true;
                }
            }
            catch { /* ignore and fallback */ }
        }
        if (!found)
        {
            var files = Directory.GetFiles(schemaDir, "*.json")
                .Where(f => !string.Equals(Path.GetFileName(f), "index.json", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var fi in files.Select(f => new FileInfo(f)).OrderByDescending(fi => fi.LastWriteTimeUtc))
            {
                try
                {
                    using var fs = File.OpenRead(fi.FullName);
                    using var doc = JsonDocument.Parse(fs);
                    if (doc.RootElement.TryGetProperty("UserDefinedTableTypes", out var monolithUdtts) && monolithUdtts.ValueKind == JsonValueKind.Array)
                    {
                        udtts = monolithUdtts;
                        found = true;
                        break;
                    }
                }
                catch { /* continue search */ }
            }
        }
        if (!found)
        {
            // Erweiterung: Expanded Layout kann eigene tabletypes/*.json Dateien besitzen.
            var tableTypesDir = Path.Combine(schemaDir, "tabletypes");
            if (Directory.Exists(tableTypesDir))
            {
                var ttFiles = Directory.GetFiles(tableTypesDir, "*.json");
                var listT = new List<TableTypeInfo>();
                foreach (var tf in ttFiles)
                {
                    try
                    {
                        using var tfs = File.OpenRead(tf);
                        using var tdoc = JsonDocument.Parse(tfs);
                        var root = tdoc.RootElement;
                        var schema = root.GetPropertyOrDefault("Schema") ?? "dbo";
                        var name = root.GetPropertyOrDefault("Name") ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var cols = new List<ColumnInfo>();
                        if (root.TryGetProperty("Columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
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
                        listT.Add(new TableTypeInfo { Schema = schema, Name = name, Columns = cols });
                    }
                    catch { /* skip file */ }
                }
                if (listT.Count > 0)
                {
                    _cache = listT.OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
                    return _cache; // success via expanded folder
                }
            }
            // Fallback: Rekonstruktion über procedure Inputs
            var procDir = Path.Combine(schemaDir, "procedures");
            if (!Directory.Exists(procDir)) return _cache = Array.Empty<TableTypeInfo>();
            var procFiles = Directory.GetFiles(procDir, "*.json");
            var inferred = new Dictionary<string, TableTypeInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var pf in procFiles)
            {
                try
                {
                    using var pfs = File.OpenRead(pf);
                    using var pdoc = JsonDocument.Parse(pfs);
                    var root = pdoc.RootElement;
                    var procSchema = root.GetPropertyOrDefault("Schema") ?? "dbo";
                    if ((root.TryGetProperty("Parameters", out var inputsEl) && inputsEl.ValueKind == JsonValueKind.Array) ||
                        (root.TryGetProperty("Inputs", out inputsEl) && inputsEl.ValueKind == JsonValueKind.Array))
                    {
                        foreach (var ip in inputsEl.EnumerateArray())
                        {
                            bool isTt = ip.GetPropertyOrDefaultBool("IsTableType");
                            if (!isTt) continue;
                            var ttSchema = ip.GetPropertyOrDefault("TableTypeSchema") ?? procSchema;
                            var ttName = ip.GetPropertyOrDefault("TableTypeName") ?? ip.GetPropertyOrDefault("Name")?.TrimStart('@') ?? "";
                            if (string.IsNullOrWhiteSpace(ttName)) continue;
                            var key = ttSchema + "." + ttName;
                            if (!inferred.ContainsKey(key))
                            {
                                inferred[key] = new TableTypeInfo { Schema = ttSchema, Name = ttName, Columns = Array.Empty<ColumnInfo>() };
                            }
                        }
                    }
                }
                catch { }
            }
            if (inferred.Count == 0) return _cache = Array.Empty<TableTypeInfo>();
            _cache = inferred.Values.OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
            return _cache;
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
    {
        if (!el.TryGetProperty(name, out var v)) return false; // fehlend -> false
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        // Bool kann auch als Zahl oder String kodiert sein (robuste Fallbacks)
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return false;
        }
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt32(out var i)) return i != 0;
            return false;
        }
        return false;
    }
    public static int? GetPropertyOrDefaultInt(this JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}

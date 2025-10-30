using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.SpocRVNext.Metadata;

internal interface ITableMetadataCache
{
    IReadOnlyList<TableInfo> GetAll();
    TableInfo? TryGet(string schema, string name);
    void Invalidate();
}

internal static class TableMetadataCacheRegistry
{
    private static readonly ConcurrentDictionary<string, TableMetadataCache> Caches = new(StringComparer.OrdinalIgnoreCase);

    public static TableMetadataCache GetOrCreate(string projectRoot, TimeSpan? ttl = null)
    {
        var normalized = NormalizeRoot(projectRoot);
        return Caches.GetOrAdd(normalized, _ => new TableMetadataCache(normalized, ttl ?? TableMetadataCache.DefaultTtl));
    }

    public static void Invalidate(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        var normalized = NormalizeRoot(projectRoot);
        if (Caches.TryGetValue(normalized, out var cache))
        {
            cache.Invalidate();
        }
    }

    private static string NormalizeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(path);
    }
}

internal sealed class TableMetadataCache : ITableMetadataCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly string _projectRoot;
    private readonly string _tablesDirectory;
    private readonly TimeSpan _ttl;
    private CacheSnapshot _snapshot = CacheSnapshot.Empty;

    public TableMetadataCache(string projectRoot, TimeSpan ttl)
    {
        _projectRoot = projectRoot;
        _tablesDirectory = Path.Combine(projectRoot, ".spocr", "schema", "tables");
        _ttl = ttl < TimeSpan.Zero ? TimeSpan.Zero : ttl;
    }

    public IReadOnlyList<TableInfo> GetAll()
    {
        return EnsureSnapshot().Tables;
    }

    public TableInfo? TryGet(string schema, string name)
    {
        var snapshot = EnsureSnapshot();
        var key = BuildKey(schema, name);
        return snapshot.Map.TryGetValue(key, out var table) ? table : null;
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _snapshot = CacheSnapshot.Empty;
        }
    }

    private CacheSnapshot EnsureSnapshot()
    {
        var now = DateTime.UtcNow;
        var directoryTimestamp = GetDirectoryTimestamp();
        var snapshot = _snapshot;
        if (snapshot.IsValid(now, _ttl, directoryTimestamp))
        {
            return snapshot;
        }

        lock (_sync)
        {
            now = DateTime.UtcNow;
            directoryTimestamp = GetDirectoryTimestamp();
            snapshot = _snapshot;
            if (!snapshot.IsValid(now, _ttl, directoryTimestamp))
            {
                snapshot = Load(now);
                _snapshot = snapshot;
            }
            return snapshot;
        }
    }

    private CacheSnapshot Load(DateTime utcNow)
    {
        if (!Directory.Exists(_tablesDirectory))
        {
            return new CacheSnapshot(Array.Empty<TableInfo>(), new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase), utcNow, DateTime.MinValue);
        }

        var files = Directory.GetFiles(_tablesDirectory, "*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            var baseline = GetDirectoryTimestamp();
            return new CacheSnapshot(Array.Empty<TableInfo>(), new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase), utcNow, baseline);
        }

        var resolver = new TypeMetadataResolver(_projectRoot);
        var list = new List<TableInfo>(files.Length);
        DateTime latestWrite = DateTime.MinValue;

        foreach (var file in files)
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var document = JsonDocument.Parse(stream);
                var table = ParseTable(document.RootElement, resolver);
                if (table != null)
                {
                    list.Add(table);
                }
            }
            catch
            {
                // ignore parse issues; remaining tables still cached
            }

            try
            {
                var modified = File.GetLastWriteTimeUtc(file);
                if (modified > latestWrite)
                {
                    latestWrite = modified;
                }
            }
            catch
            {
                // ignore timestamp failures
            }
        }

        var ordered = list
            .OrderBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var map = ordered.ToDictionary(t => BuildKey(t.Schema, t.Name), StringComparer.OrdinalIgnoreCase);
        var directoryTimestamp = GetDirectoryTimestamp();
        if (latestWrite > directoryTimestamp)
        {
            directoryTimestamp = latestWrite;
        }

        return new CacheSnapshot(ordered, map, utcNow, directoryTimestamp);
    }

    private static TableInfo? ParseTable(JsonElement root, TypeMetadataResolver resolver)
    {
        var schema = root.GetPropertyOrDefault("Schema") ?? root.GetPropertyOrDefault("SchemaName") ?? "dbo";
        var name = root.GetPropertyOrDefault("Name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var columns = new List<ColumnInfo>();
        if (root.TryGetProperty("Columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var column in colsEl.EnumerateArray())
            {
                var typeRef = column.GetPropertyOrDefault("TypeRef");
                var maxLen = column.GetPropertyOrDefaultInt("MaxLength");
                var precision = column.GetPropertyOrDefaultInt("Precision");
                var scale = column.GetPropertyOrDefaultInt("Scale");
                var resolved = resolver.Resolve(typeRef, maxLen, precision, scale);
                var baseType = (column.GetPropertyOrDefault("SqlTypeName") ?? column.GetPropertyOrDefault("SqlType") ?? string.Empty).Trim();
                var sqlType = resolved?.SqlType ?? baseType;
                if (string.IsNullOrWhiteSpace(sqlType) && !string.IsNullOrWhiteSpace(typeRef))
                {
                    sqlType = typeRef!;
                }

                columns.Add(new ColumnInfo
                {
                    Name = column.GetPropertyOrDefault("Name") ?? string.Empty,
                    TypeRef = typeRef ?? string.Empty,
                    SqlType = sqlType,
                    IsNullable = column.GetPropertyOrDefaultBoolStrict("IsNullable"),
                    MaxLength = resolved?.MaxLength ?? maxLen,
                    Precision = resolved?.Precision ?? precision,
                    Scale = resolved?.Scale ?? scale,
                    IsIdentity = column.GetPropertyOrDefaultBool("IsIdentity")
                });
            }
        }

        return new TableInfo
        {
            Schema = schema,
            Name = name,
            Columns = columns
        };
    }

    private DateTime GetDirectoryTimestamp()
    {
        try
        {
            if (!Directory.Exists(_tablesDirectory))
            {
                return DateTime.MinValue;
            }

            return Directory.GetLastWriteTimeUtc(_tablesDirectory);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string BuildKey(string schema, string name)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return name ?? string.Empty;
        }

        return string.Concat(schema, ".", name);
    }

    private readonly struct CacheSnapshot
    {
        public static readonly CacheSnapshot Empty = new(Array.Empty<TableInfo>(), new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase), DateTime.MinValue, DateTime.MinValue);

        public CacheSnapshot(IReadOnlyList<TableInfo> tables, Dictionary<string, TableInfo> map, DateTime loadedUtc, DateTime directoryTimestampUtc)
        {
            Tables = tables;
            Map = map;
            LoadedUtc = loadedUtc;
            DirectoryTimestampUtc = directoryTimestampUtc;
        }

        public IReadOnlyList<TableInfo> Tables { get; }
        public Dictionary<string, TableInfo> Map { get; }
        public DateTime LoadedUtc { get; }
        public DateTime DirectoryTimestampUtc { get; }

        public bool IsValid(DateTime now, TimeSpan ttl, DateTime currentDirectoryTimestamp)
        {
            if (currentDirectoryTimestamp != DirectoryTimestampUtc)
            {
                return false;
            }

            if (ttl <= TimeSpan.Zero)
            {
                return Tables.Count > 0;
            }

            if (LoadedUtc == DateTime.MinValue)
            {
                return false;
            }

            return now - LoadedUtc < ttl;
        }
    }
}

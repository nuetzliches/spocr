using System;
using System.Collections.Generic;
using System.IO;
using SpocR.SpocRVNext.Metadata; // ColumnInfo reuse

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
    private readonly ITableMetadataCache _cache;

    public TableMetadataProvider(string? projectRoot = null, TimeSpan? ttl = null)
    {
        _projectRoot = string.IsNullOrWhiteSpace(projectRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectRoot!);
        _cache = TableMetadataCacheRegistry.GetOrCreate(_projectRoot, ttl);
    }

    public IReadOnlyList<TableInfo> GetAll()
    {
        return _cache.GetAll();
    }

    public TableInfo? TryGet(string schema, string name)
    {
        return _cache.TryGet(schema, name);
    }

    public void Invalidate()
    {
        _cache.Invalidate();
    }
}


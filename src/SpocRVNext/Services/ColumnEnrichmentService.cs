using System;
using System.Collections.Generic;
using System.Linq;
using SpocR.SpocRVNext.Models;

namespace SpocR.SpocRVNext.Services;

/// <summary>
/// Post-Snapshot zentrale Spalten-Enrichment-Pipeline (Phase nach Tabellen/View-Ladung).
/// Ziel: Ergänzt fehlende SqlTypeName / Nullability in Funktions-JSON-Spalten anhand Tabellen-Metadaten.
/// Erweiterbar für Views / zukünftige ResultSet-Typen.
/// </summary>
public sealed class ColumnEnrichmentService
{
    public void EnrichFunctions(SchemaSnapshot snapshot, IConsoleService console)
    {
        if (snapshot?.Functions == null || snapshot.Functions.Count == 0) return;
        // Baue TableLookup falls Tabellen vorhanden
        var tableLookup = new Dictionary<string, Dictionary<string, (string TypeRef, bool? IsNullable, int? MaxLength)>>(StringComparer.OrdinalIgnoreCase);
        if (snapshot.Tables != null)
        {
            foreach (var t in snapshot.Tables)
            {
                var key = t.Schema + "." + t.Name;
                var colMap = new Dictionary<string, (string, bool?, int?)>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in t.Columns ?? new List<SnapshotTableColumn>())
                {
                    if (!string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.TypeRef))
                        colMap[c.Name] = (c.TypeRef!, c.IsNullable, c.MaxLength);
                }
                tableLookup[key] = colMap;
            }
        }
        int enriched = 0;
        foreach (var f in snapshot.Functions.Where(fn => fn.ReturnsJson == true && fn.Columns != null && fn.Columns.Count > 0))
        {
            foreach (var col in f.Columns!)
            {
                EnrichRecursive(f, col, tableLookup, ref enriched);
            }
        }
        console.Verbose($"[fn-enrich-post] enrichedColumns={enriched}");
    }

    private static void EnrichRecursive(SnapshotFunction fn, SnapshotFunctionColumn col,
        Dictionary<string, Dictionary<string, (string SqlType, bool? IsNullable, int? MaxLength)>> tableLookup,
        ref int enriched)
    {
        // Skip wenn bereits konkreter Typ (kein Container 'json')
        if (!string.IsNullOrWhiteSpace(col.TypeRef))
        {
            if (col.Columns != null) foreach (var child in col.Columns) EnrichRecursive(fn, child, tableLookup, ref enriched);
            return;
        }
        var leaf = (col.Name?.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()) ?? col.Name;
        // gezielte Mappings: displayName, initials, userId, rowVersion
        TryMap("identity.User", leaf, col, tableLookup, ref enriched);
        if (leaf.Equals("displayName", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(col.TypeRef)) TryMap("identity.User", "UserName", col, tableLookup, ref enriched); // Fallback
        }
        else if (leaf.Equals("rowVersion", StringComparison.OrdinalIgnoreCase))
        {
            // rowVersion Sonderfall: falls nicht gemappt -> stabile Fallback-Type
            if (string.IsNullOrWhiteSpace(col.TypeRef)) { col.TypeRef = CombineTypeRef("sys", "rowversion"); enriched++; }
        }
        if (col.Columns != null) foreach (var child in col.Columns) EnrichRecursive(fn, child, tableLookup, ref enriched);
    }

    private static void TryMap(string tableKey, string columnName, SnapshotFunctionColumn target,
        Dictionary<string, Dictionary<string, (string TypeRef, bool? IsNullable, int? MaxLength)>> tableLookup,
        ref int enriched)
    {
        if (string.IsNullOrWhiteSpace(target.TypeRef) &&
            tableLookup.TryGetValue(tableKey, out var cols) &&
            cols.TryGetValue(columnName, out var meta))
        {
            target.TypeRef = meta.TypeRef;
            if (!target.IsNullable.HasValue) target.IsNullable = meta.IsNullable;
            if (!target.MaxLength.HasValue) target.MaxLength = meta.MaxLength;
            enriched++;
        }
    }

    private static string CombineTypeRef(string schema, string name)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name)) return string.Empty;
        return string.Concat(schema.Trim(), ".", name.Trim());
    }
}

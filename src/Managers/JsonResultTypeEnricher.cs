using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpocR.DataContext;
using SpocR.DataContext.Models;
using SpocR.DataContext.Queries;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

/// <summary>
/// Aggregated statistics for JSON result type enrichment across all procedures in a pull run.
/// </summary>
public sealed class JsonTypeEnrichmentStats
{
    public long ResolvedColumns { get; private set; }
    public long NewConcrete { get; private set; }
    public long Upgrades { get; private set; }
    public void Accumulate(int resolved, int newlyConcrete, int upgrades)
    {
        ResolvedColumns += resolved;
        NewConcrete += newlyConcrete;
        Upgrades += upgrades;
    }
}

/// <summary>
/// Stage 2 JSON column type enrichment. Resolves unresolved JSON result columns to concrete SQL types
/// by inspecting base table metadata. Also performs fallback -> concrete upgrades (parser v4).
/// </summary>
public sealed class JsonResultTypeEnricher
{
    private readonly DbContext _db;
    private readonly IConsoleService _console;
    public JsonResultTypeEnricher(DbContext db, IConsoleService console)
    { _db = db; _console = console; }

    public async Task EnrichAsync(StoredProcedureModel sp, bool verbose, JsonTypeLogLevel level, JsonTypeEnrichmentStats stats, System.Threading.CancellationToken ct)
    {
        var sets = sp.Content?.ResultSets;
        if (sets == null || sets.Count == 0) return;
        var jsonSets = sets.Where(s => s.ReturnsJson && s.Columns != null && s.Columns.Count > 0).ToList();
        if (jsonSets.Count == 0) return;
        var tableCache = new Dictionary<string, List<Column>>(StringComparer.OrdinalIgnoreCase);
        var anyModified = false;
        var newSetsStage2 = new List<StoredProcedureContentModel.ResultSet>();
        var loggedColumnResolutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int loggedUpgrades = 0;
        int loggedNewConcrete = 0;
        foreach (var set in sets)
        {
            if (!set.ReturnsJson || set.Columns == null || set.Columns.Count == 0)
            { newSetsStage2.Add(set); continue; }
            var newCols = new List<StoredProcedureContentModel.ResultColumn>();
            var modifiedLocal = false;
            foreach (var col in set.Columns)
            {
                bool hadFallback = string.Equals(col.SqlTypeName, "nvarchar(max)", StringComparison.OrdinalIgnoreCase) && set.ReturnsJson;
                bool hasConcrete = !string.IsNullOrWhiteSpace(col.SqlTypeName) && !hadFallback;
                if (hasConcrete) { newCols.Add(col); continue; }
                if (!string.IsNullOrWhiteSpace(col.SourceSchema) && !string.IsNullOrWhiteSpace(col.SourceTable) && !string.IsNullOrWhiteSpace(col.SourceColumn))
                {
                    var tblKey = ($"{col.SourceSchema}.{col.SourceTable}");
                    if (!tableCache.TryGetValue(tblKey, out var tblColumns))
                    {
                        try { tblColumns = await _db.TableColumnsListAsync(col.SourceSchema, col.SourceTable, ct); }
                        catch { tblColumns = new List<Column>(); }
                        tableCache[tblKey] = tblColumns;
                    }
                    var match = tblColumns.FirstOrDefault(c => c.Name.Equals(col.SourceColumn, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        modifiedLocal = true;
                        var logTag = hadFallback ? "[json-type-upgrade]" : "[json-type-table]";
                        var logKey = $"{sp.SchemaName}.{sp.Name}|{col.Name}->{match.SqlTypeName}";
                        if (verbose && level == JsonTypeLogLevel.Detailed && !loggedColumnResolutions.Contains(logKey))
                        {
                            loggedColumnResolutions.Add(logKey);
                            if (hadFallback) loggedUpgrades++; else loggedNewConcrete++;
                            _console.Verbose($"{logTag} {sp.SchemaName}.{sp.Name} {col.Name} -> {match.SqlTypeName}");
                        }
                        newCols.Add(new StoredProcedureContentModel.ResultColumn
                        {
                            JsonPath = col.JsonPath,
                            Name = col.Name,
                            SourceSchema = col.SourceSchema,
                            SourceTable = col.SourceTable,
                            SourceColumn = col.SourceColumn,
                            SqlTypeName = match.SqlTypeName,
                            IsNullable = match.IsNullable,
                            MaxLength = match.MaxLength
                        });
                        continue;
                    }
                }
                newCols.Add(col);
            }
            if (modifiedLocal)
            {
                anyModified = true;
                newSetsStage2.Add(new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = set.ReturnsJson,
                    ReturnsJsonArray = set.ReturnsJsonArray,
                    ReturnsJsonWithoutArrayWrapper = set.ReturnsJsonWithoutArrayWrapper,
                    JsonRootProperty = set.JsonRootProperty,
                    Columns = newCols.ToArray()
                });
            }
            else { newSetsStage2.Add(set); }
        }
        if (anyModified)
        {
            sp.Content = new StoredProcedureContentModel
            {
                Definition = sp.Content.Definition,
                Statements = sp.Content.Statements ?? Array.Empty<string>(),
                ContainsSelect = sp.Content.ContainsSelect,
                ContainsInsert = sp.Content.ContainsInsert,
                ContainsUpdate = sp.Content.ContainsUpdate,
                ContainsDelete = sp.Content.ContainsDelete,
                ContainsMerge = sp.Content.ContainsMerge,
                ContainsOpenJson = sp.Content.ContainsOpenJson,
                ResultSets = newSetsStage2.ToArray(),
                UsedFallbackParser = sp.Content.UsedFallbackParser,
                ParseErrorCount = sp.Content.ParseErrorCount,
                FirstParseError = sp.Content.FirstParseError
            };
        }
        if (verbose && loggedColumnResolutions.Count > 0 && level != JsonTypeLogLevel.Off)
        {
            _console.Verbose($"[json-type-summary] {sp.SchemaName}.{sp.Name} resolved {loggedColumnResolutions.Count} columns (new={loggedNewConcrete}, upgrades={loggedUpgrades})");
        }
        stats?.Accumulate(loggedColumnResolutions.Count, loggedNewConcrete, loggedUpgrades);
    }
}

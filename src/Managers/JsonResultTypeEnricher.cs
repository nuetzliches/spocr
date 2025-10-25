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

        // Recursive processor for a single column (and its nested columns)
        async Task<(StoredProcedureContentModel.ResultColumn Column, bool Modified)> ProcessColumnAsync(StoredProcedureContentModel.ResultColumn col, bool parentReturnsJson, StoredProcedureContentModel.ResultSet owningSet, System.Threading.CancellationToken token)
        {
            if (col == null) return (new StoredProcedureContentModel.ResultColumn(), false);
            bool isJsonContext = parentReturnsJson || col.ReturnsJson == true; // treat nested container as JSON context for fallback typing
            bool hasSourceBinding = !string.IsNullOrWhiteSpace(col.SourceSchema) && !string.IsNullOrWhiteSpace(col.SourceTable) && !string.IsNullOrWhiteSpace(col.SourceColumn);
            if (hasSourceBinding) loggedColumnResolutions.Add($"__bind_only__{Guid.NewGuid():N}"); // binding progress
            bool modifiedLocal = false;

            // Removed UDTT 'record' mapping heuristic: Only AST-bound table metadata allowed. Leaving UserType* empty unless future AST capture implemented.
            // JSON_QUERY heuristic
            if (col.ExpressionKind == StoredProcedureContentModel.ResultColumnExpressionKind.JsonQuery && string.IsNullOrWhiteSpace(col.SqlTypeName))
            {
                col.SqlTypeName = "nvarchar(max)";
                if (col.IsNullable == null) col.IsNullable = true;
                modifiedLocal = true;
                if (verbose && level == JsonTypeLogLevel.Detailed)
                    _console.Verbose($"[json-type-jsonquery] {sp.SchemaName}.{sp.Name} {col.Name} -> nvarchar(max)");
            }
            // Forced nullable adjustment (outer join heuristic)
            if (col.ForcedNullable == true && (col.IsNullable == false || col.IsNullable == null))
            {
                col.IsNullable = true; modifiedLocal = true;
                if (verbose && level == JsonTypeLogLevel.Detailed)
                    _console.Verbose($"[json-type-nullable-adjust] {sp.SchemaName}.{sp.Name} {col.Name} forced nullable (outer join)");
            }
            // CAST / CONVERT target typing
            if (col.ExpressionKind == StoredProcedureContentModel.ResultColumnExpressionKind.Cast && string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(col.CastTargetType))
            {
                col.SqlTypeName = col.CastTargetType; modifiedLocal = true;
                if (verbose && level == JsonTypeLogLevel.Detailed)
                    _console.Verbose($"[json-type-cast] {sp.SchemaName}.{sp.Name} {col.Name} -> {col.SqlTypeName}");
            }

            bool hadFallback = (string.Equals(col.SqlTypeName, "nvarchar(max)", StringComparison.OrdinalIgnoreCase) || string.Equals(col.SqlTypeName, "unknown", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(col.SqlTypeName)) && isJsonContext;
            // Treat heuristic nvarchar with inline length (pattern nvarchar(x)) as upgrade candidates when binding available
            bool looksHeuristic = !string.IsNullOrWhiteSpace(col.SqlTypeName) && col.SqlTypeName.StartsWith("nvarchar(", StringComparison.OrdinalIgnoreCase);
            bool hasConcrete = !string.IsNullOrWhiteSpace(col.SqlTypeName) && !hadFallback && !looksHeuristic;
            if (hasSourceBinding && col.ExpressionKind != StoredProcedureContentModel.ResultColumnExpressionKind.Cast)
            {
                var tblKey = ($"{col.SourceSchema}.{col.SourceTable}");
                if (!tableCache.TryGetValue(tblKey, out var tblColumns))
                {
                    try { tblColumns = await _db.TableColumnsListAsync(col.SourceSchema, col.SourceTable, token); }
                    catch { tblColumns = new List<Column>(); }
                    tableCache[tblKey] = tblColumns;
                }
                // Für Dot-Aliase (z.B. type.typeId) versucht SourceColumn ggf. das volle Alias – fallback: letztes Segment gegen Tabellenspalten
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
                    col = new StoredProcedureContentModel.ResultColumn
                    {
                        Name = col.Name,
                        SourceSchema = col.SourceSchema,
                        SourceTable = col.SourceTable,
                        SourceColumn = col.SourceColumn,
                        SqlTypeName = match.SqlTypeName,
                        IsNullable = match.IsNullable,
                        MaxLength = match.MaxLength,
                        SourceAlias = col.SourceAlias,
                        ExpressionKind = col.ExpressionKind,
                        IsNestedJson = col.IsNestedJson,
                        ReturnsJson = col.ReturnsJson,
                        ReturnsJsonArray = col.ReturnsJsonArray,
                        // removed flag
                        JsonRootProperty = col.JsonRootProperty,
                        Columns = col.Columns, // will be processed below if present
                        ForcedNullable = col.ForcedNullable,
                        IsAmbiguous = col.IsAmbiguous,
                        UserTypeSchemaName = col.UserTypeSchemaName,
                        UserTypeName = col.UserTypeName
                    };
                }
                else if (!hasConcrete)
                {
                    // AST-only: signal unresolved type for JSON column; no name-based fallback
                    _console.Warn($"[json-type-miss] {sp.SchemaName}.{sp.Name} {col.Name} source={col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} no-column-match");
                }
            }
            // No name-based targeted fallbacks (AST-only policy)
            // Gemeinsame Heuristik anwenden falls weiterhin kein konkreter Typ bestimmt (kein Binding oder Binding ohne Treffer)
            if (!hasConcrete)
            {
                // Adapter für Inferenz (nutzt Name + ExpressionKind -> SourceSql aktuell nicht verfügbar im Enricher)
                var pseudo = new JsonFunctionAstColumn
                {
                    Name = col.Name,
                    IsNestedJson = col.IsNestedJson == true,
                    ReturnsJson = col.ReturnsJson == true,
                    ReturnsJsonArray = col.ReturnsJsonArray
                };
                // Heuristik entfernt: kein Name-basiertes Typ Raten mehr. Ungebundene / uncasted Spalten behalten leeren Typ.
            }

            // Recurse into nested JSON columns
            if ((col.IsNestedJson == true || col.ReturnsJson == true) && col.Columns != null && col.Columns.Count > 0)
            {
                var nestedModifiedAny = false;
                var newNested = new List<StoredProcedureContentModel.ResultColumn>();
                foreach (var nc in col.Columns)
                {
                    var processed = await ProcessColumnAsync(nc, col.ReturnsJson == true, owningSet, token);
                    if (processed.Modified) nestedModifiedAny = true;
                    newNested.Add(processed.Column);
                }
                if (nestedModifiedAny)
                {
                    // ensure we carry updated nested list
                    col = new StoredProcedureContentModel.ResultColumn
                    {
                        Name = col.Name,
                        SourceSchema = col.SourceSchema,
                        SourceTable = col.SourceTable,
                        SourceColumn = col.SourceColumn,
                        SqlTypeName = col.SqlTypeName,
                        IsNullable = col.IsNullable,
                        MaxLength = col.MaxLength,
                        SourceAlias = col.SourceAlias,
                        ExpressionKind = col.ExpressionKind,
                        IsNestedJson = col.IsNestedJson,
                        ReturnsJson = col.ReturnsJson,
                        ReturnsJsonArray = col.ReturnsJsonArray,
                        // removed flag
                        JsonRootProperty = col.JsonRootProperty,
                        Columns = newNested,
                        ForcedNullable = col.ForcedNullable,
                        IsAmbiguous = col.IsAmbiguous,
                        UserTypeSchemaName = col.UserTypeSchemaName,
                        UserTypeName = col.UserTypeName
                    };
                    modifiedLocal = true;
                }
            }
            return (col, modifiedLocal);
        }

        foreach (var set in sets)
        {
            if (!set.ReturnsJson || set.Columns == null || set.Columns.Count == 0)
            { newSetsStage2.Add(set); continue; }
            var newCols = new List<StoredProcedureContentModel.ResultColumn>();
            var modifiedLocal = false;
            foreach (var col in set.Columns)
            {
                var processed = await ProcessColumnAsync(col, set.ReturnsJson, set, ct);
                if (processed.Modified) modifiedLocal = true;
                newCols.Add(processed.Column);
            }
            if (modifiedLocal)
            {
                anyModified = true;
                newSetsStage2.Add(new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = set.ReturnsJson,
                    ReturnsJsonArray = set.ReturnsJsonArray,
                    // removed flag
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
        else if (verbose && level == JsonTypeLogLevel.Detailed)
        {
            // still emit a summary line even if no resolutions, to show nullable adjustments / jsonquery activity
            _console.Verbose($"[json-type-summary] {sp.SchemaName}.{sp.Name} no new resolutions");
        }
        stats?.Accumulate(loggedColumnResolutions.Count, loggedNewConcrete, loggedUpgrades);
    }
}

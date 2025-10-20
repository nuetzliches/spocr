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
            int localResolvedBindings = 0; // count of columns having Source* even if no upgrade yet
            foreach (var col in set.Columns)
            {
                bool hasSourceBinding = !string.IsNullOrWhiteSpace(col.SourceSchema) && !string.IsNullOrWhiteSpace(col.SourceTable) && !string.IsNullOrWhiteSpace(col.SourceColumn);
                if (hasSourceBinding) localResolvedBindings++; // record source binding progress
                // UDTT context mapping heuristic: map placeholder column 'record' to existing Context table-valued input
                if (string.Equals(col.Name, "record", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(col.UserTypeName) && string.IsNullOrWhiteSpace(col.UserTypeSchemaName)))
                {
                    var contextInput = sp.Input?.FirstOrDefault(i => i.IsTableType == true &&
                        string.Equals(i.TableTypeName, "Context", StringComparison.OrdinalIgnoreCase));
                    if (contextInput != null)
                    {
                        col.UserTypeSchemaName = contextInput.TableTypeSchemaName ?? "core"; // default schema fallback
                        col.UserTypeName = contextInput.TableTypeName;
                        modifiedLocal = true;
                        if (verbose && level == JsonTypeLogLevel.Detailed)
                        {
                            _console.Verbose($"[json-type-udtt-ref] {sp.SchemaName}.{sp.Name} {col.Name} -> {col.UserTypeSchemaName}.{col.UserTypeName}");
                        }
                    }
                }
                // v5: Apply JSON_QUERY default typing & join nullability adjustment before main resolution logic
                if (col.ExpressionKind == StoredProcedureContentModel.ResultColumnExpressionKind.JsonQuery && string.IsNullOrWhiteSpace(col.SqlTypeName))
                {
                    col.SqlTypeName = "nvarchar(max)"; // JSON_QUERY always returns NVARCHAR(MAX)
                    if (col.IsNullable == null) col.IsNullable = true; // JSON_QUERY may yield NULL
                    modifiedLocal = true;
                    if (verbose && level == JsonTypeLogLevel.Detailed)
                    {
                        _console.Verbose($"[json-type-jsonquery] {sp.SchemaName}.{sp.Name} {col.Name} -> nvarchar(max)");
                    }
                }
                if (col.ForcedNullable == true && (col.IsNullable == false || col.IsNullable == null))
                {
                    col.IsNullable = true;
                    modifiedLocal = true;
                    if (verbose && level == JsonTypeLogLevel.Detailed)
                    {
                        _console.Verbose($"[json-type-nullable-adjust] {sp.SchemaName}.{sp.Name} {col.Name} forced nullable (outer join)");
                    }
                }
                // v5: CAST/CONVERT heuristic – if we parsed a CastTargetType and still no concrete sql type
                if (col.ExpressionKind == StoredProcedureContentModel.ResultColumnExpressionKind.Cast && string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.IsNullOrWhiteSpace(col.CastTargetType))
                {
                    col.SqlTypeName = col.CastTargetType;
                    // Nullability not changed; size already embedded if present
                    modifiedLocal = true;
                    if (verbose && level == JsonTypeLogLevel.Detailed)
                    {
                        _console.Verbose($"[json-type-cast] {sp.SchemaName}.{sp.Name} {col.Name} -> {col.SqlTypeName}");
                    }
                }
                // Treat both legacy nvarchar(max) and new 'unknown' marker as fallback states
                bool hadFallback = (string.Equals(col.SqlTypeName, "nvarchar(max)", StringComparison.OrdinalIgnoreCase) || string.Equals(col.SqlTypeName, "unknown", StringComparison.OrdinalIgnoreCase)) && set.ReturnsJson;
                bool hasConcrete = !string.IsNullOrWhiteSpace(col.SqlTypeName) && !hadFallback;
                if (hasConcrete) { newCols.Add(col); continue; }
                if (hasSourceBinding)
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
                            MaxLength = match.MaxLength,
                            SourceAlias = col.SourceAlias,
                            ExpressionKind = col.ExpressionKind,
                            IsNestedJson = col.IsNestedJson,
                            ForcedNullable = col.ForcedNullable,
                            IsAmbiguous = col.IsAmbiguous
                        });
                        continue;
                    }
                    else if (verbose && level == JsonTypeLogLevel.Detailed)
                    {
                        _console.Verbose($"[json-type-miss] {sp.SchemaName}.{sp.Name} {col.Name} source={col.SourceSchema}.{col.SourceTable}.{col.SourceColumn} no-column-match");
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
            // Accumulate binding-only progress (not counted previously). We treat these as 'resolved' even if still fallback typed.
            if (localResolvedBindings > 0)
            {
                // Use synthetic logKey entries so run summary counts them as ResolvedColumns when no upgrades occurred.
                // We don't want duplicate counting across sets; just add ephemeral entries.
                for (int i = 0; i < localResolvedBindings; i++) loggedColumnResolutions.Add($"__bind_only__{Guid.NewGuid():N}");
            }
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Data;
using SpocR.SpocRVNext.Data.Queries;
using SpocR.SpocRVNext.Data.Models;
using SpocR.SpocRVNext.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.SpocRVNext.Schema;

public class SchemaManager(
    DbContext dbContext,
    IConsoleService consoleService,
    ISchemaSnapshotService schemaSnapshotService,
    Services.SchemaSnapshotFileLayoutService expandedSnapshotService,
    ILocalCacheService localCacheService = null
)
{
    private static bool ShouldDiagJsonMissAst()
    {
        var lvl = Environment.GetEnvironmentVariable("SPOCR_LOG_LEVEL");
        if (string.Equals(lvl, "trace", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(lvl, "debug", StringComparison.OrdinalIgnoreCase)) return true;
        // Separate explicit gate if someone wants the message without full debug
        if (Environment.GetEnvironmentVariable("SPOCR_JSON_MISS_AST_DIAG") == "1") return true;
        return false;
    }
    // Logging prefixes used in this manager (overview):
    // [proc-fixup-json-reparse]   Added JSON sets after placeholder-only detection via reparse.
    // [proc-fixup-json-cols]      Extracted column names for synthetic JSON set.
    // [proc-fixup-json-synth]     Synthesized minimal JSON result set when only EXEC placeholder existed.
    // [proc-prune-json-nested]    Removed a nested FOR JSON expression-derived (inline) result set (not a real top-level output).
    // [proc-prune-json-nested-warn] Heuristic prune failure (exception swallowed, verbose only).
    // [proc-forward-multi]        Inserted multiple ExecSource placeholders for wrapper with >1 EXECs.
    // [proc-forward-refonly]      Inserted single ExecSource placeholder preserving local sets (reference-only forwarding).
    // [proc-forward-replace]      Replaced placeholder(s) entirely with forwarded target sets.
    // [proc-forward-append]       Appended forwarded sets after existing local sets.
    // [proc-forward-xschema]      Forwarded sets from cross-schema target (snapshot or direct load).
    // [proc-forward-xschema-upgrade] Variation when upgrading empty JSON placeholders.
    // [proc-exec-append]          Appended target sets for non-wrapper with single EXEC.
    // [proc-exec-append-xschema]  Cross-schema append variant.
    // [proc-wrapper-posthoc]      Post-hoc normalization to single ExecSource placeholder for pure wrapper.
    // [proc-dedupe]               Removed duplicate result sets after forwarding/append phases.
    // [proc-exec-miss]            EXEC keyword seen but AST failed to capture executed procedure (diagnostic).
    // [ignore]                    Schema ignore processing diagnostics.
    // [cache]                     Local cache snapshot load/save events.
    // [timing]                    Overall timing diagnostics.
    // These prefixes allow downstream log consumers to filter transformation phases precisely.
    public async Task<List<SchemaModel>> ListAsync(ConfigurationModel config, bool noCache = false, CancellationToken cancellationToken = default)
    {
        // Ensure AST parser can resolve table column types for CTE type propagation into nested JSON
        if (StoredProcedureContentModel.ResolveTableColumnType == null)
        {
            // Prefer snapshot metadata (expanded) over live DB calls. Tables/Views/UDTTs/UDTs are loaded before procedures.
            var tableMeta = new SpocR.SpocRVNext.Metadata.TableMetadataProvider(DirectoryUtils.GetWorkingDirectory());
            var tableIndex = tableMeta.GetAll()?.GroupBy(t => t.Schema + "." + t.Name, StringComparer.OrdinalIgnoreCase)
                                      .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, SpocR.SpocRVNext.Metadata.TableInfo>(StringComparer.OrdinalIgnoreCase);

            StoredProcedureContentModel.ResolveTableColumnType = (schema, table, column) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column))
                        return (string.Empty, null, null);
                    var key = schema + "." + table;
                    if (tableIndex.TryGetValue(key, out var ti))
                    {
                        var col = ti.Columns?.FirstOrDefault(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
                        if (col != null && !string.IsNullOrWhiteSpace(col.SqlType))
                        {
                            return (col.SqlType, col.MaxLength, col.IsNullable);
                        }
                    }
                }
                catch { }
                return (string.Empty, null, null);
            };
        }

        // Provide UDT resolver from expanded snapshot (UserDefinedTypes)
        if (StoredProcedureContentModel.ResolveUserDefinedType == null)
        {
            try
            {
                var expanded = expandedSnapshotService.LoadExpanded();
                var udtMap = new Dictionary<string, Services.SnapshotUserDefinedType>(StringComparer.OrdinalIgnoreCase);
                foreach (var u in expanded?.UserDefinedTypes ?? new List<Services.SnapshotUserDefinedType>())
                {
                    if (!string.IsNullOrWhiteSpace(u?.Schema) && !string.IsNullOrWhiteSpace(u?.Name))
                    {
                        udtMap[$"{u.Schema}.{u.Name}"] = u;
                        udtMap[u.Name] = u; // allow name-only match for common UDT schemas
                        // TODO diese Heuristik muss entfernt werden, geht es allgemein um Sonderzeichen im Namen? Dann nur durch _ ersetzen?
                        // Also register underscore-prefixed alias to support declarations like [core].[_id]
                        var underscoreAlias = u.Name.StartsWith("_") ? u.Name : ("_" + u.Name);
                        if (!udtMap.ContainsKey(underscoreAlias)) udtMap[underscoreAlias] = u;
                        var fqUnderscore = $"{u.Schema}.{underscoreAlias}";
                        if (!udtMap.ContainsKey(fqUnderscore)) udtMap[fqUnderscore] = u;
                    }
                }
                StoredProcedureContentModel.ResolveUserDefinedType = (schema, name) =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(name)) return (string.Empty, null, null, null, null);
                        var key1 = schema + "." + name;
                        if (udtMap.TryGetValue(key1, out var udt) || udtMap.TryGetValue(name, out udt))
                        {
                            var baseType = udt.BaseSqlTypeName;
                            int? maxLen = udt.MaxLength;
                            int? prec = udt.Precision;
                            int? scale = udt.Scale;
                            bool? isNull = udt.IsNullable;
                            // Honor underscore variant semantics: NOT NULL
                            if (!string.IsNullOrWhiteSpace(name) && name.StartsWith("_")) isNull = false;
                            return (baseType, maxLen, prec, scale, isNull);
                        }
                        // Fallback: strip leading underscore from UDT name if present (e.g., core._id -> core.id)
                        if (name.StartsWith("_"))
                        {
                            var trimmed = name.TrimStart('_');
                            var key2 = schema + "." + trimmed;
                            if (udtMap.TryGetValue(key2, out var udt2) || udtMap.TryGetValue(trimmed, out udt2))
                            {
                                var baseType = udt2.BaseSqlTypeName;
                                int? maxLen = udt2.MaxLength;
                                int? prec = udt2.Precision;
                                int? scale = udt2.Scale;
                                bool? isNull = false; // underscore -> NOT NULL
                                return (baseType, maxLen, prec, scale, isNull);
                            }
                        }
                    }
                    catch { }
                    return (string.Empty, null, null, null, null);
                };
            }
            catch { }
        }

        // Provide scalar function return type resolver from expanded snapshot (Functions)
        if (StoredProcedureContentModel.ResolveScalarFunctionReturnType == null)
        {
            try
            {
                var expanded = expandedSnapshotService.LoadExpanded();
                var fnMap = new Dictionary<string, (string SqlType, int? MaxLen, bool? IsNull)>(StringComparer.OrdinalIgnoreCase);
                foreach (var fn in expanded?.Functions ?? new List<Services.SnapshotFunction>())
                {
                    if (fn?.IsTableValued == true) continue; // only scalar
                    if (string.IsNullOrWhiteSpace(fn?.Schema) || string.IsNullOrWhiteSpace(fn?.Name)) continue;
                    if (string.IsNullOrWhiteSpace(fn.ReturnSqlType)) continue;
                    var key = $"{fn.Schema}.{fn.Name}";
                    // Use ReturnSqlType as-is (collector may already include precision/scale or length)
                    var resolvedType = fn.ReturnSqlType;
                    fnMap[key] = (resolvedType, fn.ReturnMaxLength, fn.ReturnIsNullable);
                    // also allow name-only lookup for common schemas
                    if (!fnMap.ContainsKey(fn.Name)) fnMap[fn.Name] = (resolvedType, fn.ReturnMaxLength, fn.ReturnIsNullable);
                }
                StoredProcedureContentModel.ResolveScalarFunctionReturnType = (schema, name) =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(name)) return (string.Empty, null, null);
                        var key1 = (schema ?? "dbo") + "." + name;
                        if (fnMap.TryGetValue(key1, out var meta) || fnMap.TryGetValue(name, out meta))
                        {
                            return (meta.SqlType, meta.MaxLen, meta.IsNull);
                        }
                    }
                    catch { }
                    return (string.Empty, null, null);
                };
            }
            catch { }
        }

        var dbSchemas = await dbContext.SchemaListAsync(cancellationToken);
        if (dbSchemas == null)
        {
            return null;
        }

        var schemas = dbSchemas?.Select(i => new SchemaModel(i)).ToList();

        // Legacy schema list (config.Schema) still present -> use its statuses first
        if (config?.Schema != null)
        {
            foreach (var schema in schemas)
            {
                var currentSchema = config.Schema.SingleOrDefault(i => i.Name == schema.Name);
                schema.Status = (currentSchema != null)
                    ? currentSchema.Status
                    : config.Project.DefaultSchemaStatus;
            }
        }
        else if (config?.Project != null)
        {
            // Snapshot-only mode (legacy schema node removed).
            // Revised semantics for DefaultSchemaStatus=Ignore:
            //   - ONLY brand new schemas (not present in the latest snapshot) are auto-ignored and added to IgnoredSchemas.
            //   - Previously known schemas default to Build unless explicitly ignored.
            // For any other default value the prior fallback behavior applies.

            var ignored = config.Project.IgnoredSchemas ?? new List<string>();
            var defaultStatus = config.Project.DefaultSchemaStatus;

            // Determine known schemas from latest snapshot (if present)
            var knownSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var working = DirectoryUtils.GetWorkingDirectory();
                var schemaDir = System.IO.Path.Combine(working, ".spocr", "schema");
                // Fixup phase: repair procedures that have exactly one EXEC placeholder but are missing a local JSON result set.
                // Scenario: parser did not capture the FOR JSON SELECT (e.g. complex construction) and after forwarding only a placeholder remains.
                // Heuristic: If definition contains "FOR JSON" and ResultSets has exactly one placeholder (empty columns, ExecSource set, ReturnsJson=false) -> attempt reparse.
                // If reparse yields no JSON sets: add a minimal synthetic empty JSON set to preserve structure for downstream generation.
                // Entfernt: automatische Placeholder-Reparse & synthetische JSON Set Erzeugung.
                // AST-only Modus: Stattdessen optionales Logging falls potentielle verpasste JSON Sets erkannt werden.
                try
                {
                    bool enableLegacyPlaceholderReparse = Environment.GetEnvironmentVariable("SPOCR_JSON_PLACEHOLDER_REPARSE") == "1";
                    foreach (var schema in schemas)
                    {
                        foreach (var proc in schema.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>())
                        {
                            var content = proc.Content;
                            if (content?.ResultSets == null) continue;
                            if (content.ResultSets.Count != 1) continue;
                            var rs0 = content.ResultSets[0];
                            bool isExecPlaceholderOnly = !rs0.ReturnsJson && !rs0.ReturnsJsonArray && !string.IsNullOrEmpty(rs0.ExecSourceProcedureName) && (rs0.Columns == null || rs0.Columns.Count == 0);
                            if (!isExecPlaceholderOnly) continue;
                            var def = content.Definition;
                            if (string.IsNullOrWhiteSpace(def)) continue;
                            if (def.IndexOf("FOR JSON", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (enableLegacyPlaceholderReparse)
                            {
                                StoredProcedureContentModel reparsed = null;
                                try { reparsed = StoredProcedureContentModel.Parse(def, proc.SchemaName); } catch { }
                                var jsonSets = reparsed?.ResultSets?.Where(r => r.ReturnsJson)?.ToList();
                                if (jsonSets != null && jsonSets.Count > 0)
                                {
                                    var newSets = new List<StoredProcedureContentModel.ResultSet> { rs0 };
                                    newSets.AddRange(jsonSets);
                                    proc.Content = new StoredProcedureContentModel
                                    {
                                        Definition = content.Definition,
                                        Statements = content.Statements,
                                        ContainsSelect = content.ContainsSelect,
                                        ContainsInsert = content.ContainsInsert,
                                        ContainsUpdate = content.ContainsUpdate,
                                        ContainsDelete = content.ContainsDelete,
                                        ContainsMerge = content.ContainsMerge,
                                        ContainsOpenJson = content.ContainsOpenJson,
                                        ResultSets = newSets,
                                        UsedFallbackParser = content.UsedFallbackParser,
                                        ParseErrorCount = content.ParseErrorCount,
                                        FirstParseError = content.FirstParseError,
                                        ExecutedProcedures = content.ExecutedProcedures,
                                        ContainsExecKeyword = content.ContainsExecKeyword,
                                        RawExecCandidates = content.RawExecCandidates,
                                        RawExecCandidateKinds = content.RawExecCandidateKinds
                                    };
                                    consoleService.Verbose($"[proc-fixup-json-reparse] {proc.SchemaName}.{proc.Name} added {jsonSets.Count} JSON set(s) (legacy mode)");
                                    continue;
                                }
                            }
                            // Nur Diagnose ausgeben (kein Synthese mehr)
                            if (ShouldDiagJsonMissAst())
                            {
                                consoleService.Output($"[proc-json-miss-ast] {proc.SchemaName}.{proc.Name} placeholder-only EXEC with FOR JSON detected but no AST JSON set (legacy reparse disabled)");
                            }
                        }
                    }
                }
                catch (Exception jsonMissEx)
                {
                    consoleService.Verbose($"[proc-json-miss-ast-warn] {jsonMissEx.Message}");
                }

                if (System.IO.Directory.Exists(schemaDir))
                {
                    var latest = System.IO.Directory.GetFiles(schemaDir, "*.json")
                        .Select(f => new System.IO.FileInfo(f))
                        .OrderByDescending(fi => fi.LastWriteTimeUtc)
                        .FirstOrDefault();
                    if (latest != null)
                    {
                        var snap = schemaSnapshotService.Load(System.IO.Path.GetFileNameWithoutExtension(latest.Name));
                        if (snap?.Schemas != null)
                        {
                            foreach (var s in snap.Schemas)
                            {
                                if (!string.IsNullOrWhiteSpace(s.Name))
                                    knownSchemas.Add(s.Name);
                            }
                        }
                    }
                }
            }
            catch { /* best effort */ }

            bool addedNewIgnored = false;
            var initialIgnoredSet = new HashSet<string>(ignored, StringComparer.OrdinalIgnoreCase); // track originally ignored for delta detection
            var autoAddedIgnored = new List<string>();

            foreach (var schema in schemas)
            {
                var isExplicitlyIgnored = ignored.Contains(schema.Name, StringComparer.OrdinalIgnoreCase);
                var isKnown = knownSchemas.Contains(schema.Name);

                if (defaultStatus == SchemaStatusEnum.Ignore)
                {
                    // FIRST RUN (no snapshot): do NOT auto-extend IgnoredSchemas.
                    if (knownSchemas.Count == 0)
                    {
                        schema.Status = isExplicitlyIgnored ? SchemaStatusEnum.Ignore : SchemaStatusEnum.Build;
                        continue;
                    }

                    // Subsequent runs: only truly new (unknown) schemas become auto-ignored.
                    if (isExplicitlyIgnored)
                    {
                        schema.Status = SchemaStatusEnum.Ignore;
                    }
                    else if (!isKnown)
                    {
                        schema.Status = SchemaStatusEnum.Ignore;
                        if (!ignored.Contains(schema.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            ignored.Add(schema.Name);
                            autoAddedIgnored.Add(schema.Name);
                            addedNewIgnored = true;
                        }
                    }
                    else
                    {
                        schema.Status = SchemaStatusEnum.Build;
                    }
                }
                else
                {
                    schema.Status = defaultStatus;
                    if (isExplicitlyIgnored)
                    {
                        schema.Status = SchemaStatusEnum.Ignore;
                    }
                }
            }

            // Update IgnoredSchemas in config (in-memory only here; persistence handled by caller)
            if (addedNewIgnored)
            {
                // Ensure list stays de-duplicated and sorted
                config.Project.IgnoredSchemas = ignored.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                consoleService.Verbose($"[ignore] Auto-added {autoAddedIgnored.Count} new schema(s) to IgnoredSchemas (default=Ignore)");
            }

            // Bootstrap heuristic removed: on first run all non-explicitly ignored schemas are built.

            if (ignored.Count > 0)
            {
                consoleService.Verbose($"[ignore] Applied IgnoredSchemas list ({ignored.Count}) (default={defaultStatus})");
            }
        }

        // If both legacy and IgnoredSchemas exist (edge case during migration), let IgnoredSchemas override
        if (config?.Schema != null && config.Project?.IgnoredSchemas?.Any() == true)
        {
            foreach (var schema in schemas)
            {
                if (config.Project.IgnoredSchemas.Contains(schema.Name, StringComparer.OrdinalIgnoreCase))
                {
                    schema.Status = SchemaStatusEnum.Ignore;
                }
            }
            consoleService.Verbose($"[ignore] IgnoredSchemas override applied ({config.Project.IgnoredSchemas.Count})");
        }

        // Reorder: ignored first (kept for legacy ordering expectations)
        schemas = schemas.OrderByDescending(schema => schema.Status).ToList();

        var activeSchemas = schemas.Where(i => i.Status != SchemaStatusEnum.Ignore).ToList();
        // Build list only for later filtering/persistence logic; we enumerate procedures for all schemas unfiltered.
        var storedProcedures = await dbContext.StoredProcedureListAsync(string.Empty, cancellationToken);
        var schemaListString = string.Join(',', activeSchemas.Select(i => $"'{i.Name}'"));

        // Apply IgnoredProcedures filter (schema.name) early
        var ignoredProcedures = config?.Project?.IgnoredProcedures ?? new List<string>();
        var jsonTypeLogLevel = config?.Project?.JsonTypeLogLevel ?? JsonTypeLogLevel.Detailed;
        if (ignoredProcedures.Count > 0)
        {
            var ignoredSet = new HashSet<string>(ignoredProcedures, StringComparer.OrdinalIgnoreCase);
            var beforeCount = storedProcedures.Count;
            storedProcedures = storedProcedures.Where(sp => !ignoredSet.Contains($"{sp.SchemaName}.{sp.Name}"))?.ToList();
            var removed = beforeCount - storedProcedures.Count;
            if (removed > 0)
            {
                consoleService.Verbose($"[ignore-proc] Filtered {removed} procedure(s) via IgnoredProcedures list");
            }
        }

        // Apply --procedure flag filtering for schema snapshots (pull command only)
        var buildProcedures = Environment.GetEnvironmentVariable("SPOCR_BUILD_PROCEDURES");
        bool hasProcedureFilter = false;
        HashSet<string> procedureFilterExact = null;
        System.Collections.Generic.List<System.Text.RegularExpressions.Regex> procedureFilterWildcard = null;
        if (!string.IsNullOrWhiteSpace(buildProcedures))
        {
            var tokens = buildProcedures.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(p => p.Trim())
                                        .Where(p => !string.IsNullOrEmpty(p))
                                        .ToList();

            procedureFilterExact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            procedureFilterWildcard = new System.Collections.Generic.List<System.Text.RegularExpressions.Regex>();
            foreach (var t in tokens)
            {
                if (t.Contains('*') || t.Contains('?'))
                {
                    // Convert wildcard to Regex: escape, then replace \* -> .*, \? -> .
                    var escaped = System.Text.RegularExpressions.Regex.Escape(t);
                    var pattern = "^" + escaped.Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    try { procedureFilterWildcard.Add(new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)); } catch { }
                }
                else
                {
                    procedureFilterExact.Add(t);
                }
            }

            hasProcedureFilter = (procedureFilterExact.Count + procedureFilterWildcard.Count) > 0;
            if (hasProcedureFilter)
            {
                var beforeCount = storedProcedures.Count;
                bool Matches(string fq)
                {
                    if (procedureFilterExact.Contains(fq)) return true;
                    if (procedureFilterWildcard.Count == 0) return false;
                    foreach (var rx in procedureFilterWildcard) { if (rx.IsMatch(fq)) return true; }
                    return false;
                }
                storedProcedures = storedProcedures.Where(sp => Matches($"{sp.SchemaName}.{sp.Name}"))?.ToList();
                var kept = storedProcedures.Count;
                if (kept != beforeCount)
                {
                    consoleService.Verbose($"[procedure-filter] Filtered to {kept} procedure(s) via --procedure flag (was {beforeCount})");
                }
            }
        }

        // Build a simple fingerprint (avoid secrets): use output namespace or role kind + schemas + SP count
        var projectId = config?.Project?.Output?.Namespace ?? config?.Project?.Role?.Kind.ToString() ?? "UnknownProject";
        var fingerprintRaw = $"{projectId}|{schemaListString}|{storedProcedures.Count}";
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprintRaw))).Substring(0, 16);

        var loadStart = DateTime.UtcNow;
        var disableCache = noCache; // Global cache toggle; per-procedure forcing handled below

        ProcedureCacheSnapshot cache = null;
        if (!disableCache && localCacheService != null)
        {
            cache = localCacheService.Load(fingerprint);
            if (cache != null)
            {
                consoleService.Verbose($"[cache] Loaded snapshot {fingerprint} with {cache.Procedures.Count} entries in {(DateTime.UtcNow - loadStart).TotalMilliseconds:F1} ms");
            }
            else
            {
                consoleService.Verbose($"[cache] No existing snapshot for {fingerprint}");
            }
        }
        else if (disableCache)
        {
            consoleService.Verbose("[cache] Disabled (--no-cache)");
        }
        var updatedSnapshot = new ProcedureCacheSnapshot { Fingerprint = fingerprint };
        var tableTypes = await dbContext.TableTypeListAsync(schemaListString, cancellationToken);
        var procedureOutputs = new Dictionary<string, List<StoredProcedureOutputModel>>(StringComparer.OrdinalIgnoreCase);

        var totalSpCount = storedProcedures.Count;
        var processed = 0;
        var lastPercentage = -1;
        if (totalSpCount > 0)
        {
            consoleService.StartProgress($"Loading Stored Procedures ({totalSpCount})");
            consoleService.DrawProgressBar(0);
        }
        // Change detection now exclusively uses local cache snapshot (previous config ignore)

        // NOTE: Current modification ticks are derived from sys.objects.modify_date (see StoredProcedure.Modified)

        // Build snapshot procedure lookup (prefer expanded layout) for hydration of skipped procedures
        Dictionary<string, Dictionary<string, SnapshotProcedure>> snapshotProcMap = null;
        try
        {
            if (!disableCache)
            {
                var working = DirectoryUtils.GetWorkingDirectory();
                var schemaDir = System.IO.Path.Combine(working, ".spocr", "schema");
                if (System.IO.Directory.Exists(schemaDir))
                {
                    SchemaSnapshot expanded = null;
                    try
                    {
                        expanded = expandedSnapshotService.LoadExpanded();
                        if (expanded?.Procedures?.Any() == true)
                        {
                            snapshotProcMap = expanded.Procedures
                                .GroupBy(p => p.Schema, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
                            consoleService.Verbose($"[snapshot-hydrate] Expanded snapshot geladen (fingerprint={expanded.Fingerprint}) procs={expanded.Procedures.Count}");
                        }
                    }
                    catch { /* best effort */ }
                }
            }
        }
        catch { /* best effort */ }

        // --- Stored Procedure Enumeration & Hydration (reconstructed cleanly) ---
        foreach (var schema in schemas)
        {
            schema.StoredProcedures = storedProcedures
                .Where(sp => sp.SchemaName.Equals(schema.Name, StringComparison.OrdinalIgnoreCase))
                .Select(sp => new StoredProcedureModel(sp))
                .ToList();
            if (schema.StoredProcedures == null) continue;

            foreach (var storedProcedure in schema.StoredProcedures)
            {
                processed++;
                if (totalSpCount > 0)
                {
                    var percentage = (processed * 100) / totalSpCount;
                    if (percentage != lastPercentage)
                    {
                        consoleService.DrawProgressBar(percentage);
                        lastPercentage = percentage;
                    }
                }

                var currentModifiedTicks = storedProcedure.Modified.Ticks;
                var cacheEntry = cache?.Procedures.FirstOrDefault(p => p.Schema == storedProcedure.SchemaName && p.Name == storedProcedure.Name);
                var previousModifiedTicks = cacheEntry?.ModifiedTicks;
                var canSkipDetails = !disableCache && previousModifiedTicks.HasValue && previousModifiedTicks.Value == currentModifiedTicks;
                // Force re-parse for procedures selected via --procedure (per-proc, not global), wildcard-aware
                if (hasProcedureFilter)
                {
                    var fq = $"{storedProcedure.SchemaName}.{storedProcedure.Name}";
                    bool Matches(string name)
                    {
                        if (procedureFilterExact != null && procedureFilterExact.Contains(name)) return true;
                        if (procedureFilterWildcard != null)
                        {
                            foreach (var rx in procedureFilterWildcard) { if (rx.IsMatch(name)) return true; }
                        }
                        return false;
                    }
                    if (Matches(fq))
                    {
                        if (canSkipDetails && jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                            consoleService.Verbose($"[cache] Re-parse forced for {fq} due to --procedure flag");
                        canSkipDetails = false;
                    }
                }
                // Skip decision: previously required snapshot hydration presence; caching test expects skip purely on modify_date stability.
                // We retain hydration usage when available but do not downgrade skip if absent.
                if (canSkipDetails && snapshotProcMap != null)
                {
                    bool hasHydration = snapshotProcMap.TryGetValue(storedProcedure.SchemaName, out var spMap) && spMap.ContainsKey(storedProcedure.Name);
                    // If hydration exists we may populate inputs/resultsets later; absence no longer forces parse.
                    if (!hasHydration && jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                    {
                        consoleService.Verbose($"[proc-skip-no-hydration] {storedProcedure.SchemaName}.{storedProcedure.Name} skipping without snapshot hydration");
                    }
                }

                if (canSkipDetails)
                {
                    if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                        consoleService.Verbose($"[proc-skip] {storedProcedure.SchemaName}.{storedProcedure.Name} unchanged (ticks={currentModifiedTicks})");
                    if (snapshotProcMap != null && snapshotProcMap.TryGetValue(storedProcedure.SchemaName, out var spMap) && spMap.TryGetValue(storedProcedure.Name, out var snapProc))
                    {
                        // Inputs hydration
                        if (snapProc.Inputs?.Any() == true && (storedProcedure.Input == null || !storedProcedure.Input.Any()))
                        {
                            storedProcedure.Input = snapProc.Inputs
                                .Select(MapSnapshotInputToModel)
                                .Where(model => model != null)
                                .Select(model => model!)
                                .ToList();
                        }
                        // ResultSets hydration
                        if (snapProc.ResultSets?.Any() == true && (storedProcedure.Content?.ResultSets == null || !storedProcedure.Content.ResultSets.Any()))
                        {
                            StoredProcedureContentModel.ResultColumn MapSnapshotColToRuntime(SnapshotResultColumn c)
                            {
                                if (c == null)
                                {
                                    return new StoredProcedureContentModel.ResultColumn();
                                }

                                var (schema, name) = SplitTypeRef(c.TypeRef);
                                var isSystemType = IsSystemSchema(schema);

                                var column = new StoredProcedureContentModel.ResultColumn
                                {
                                    Name = c.Name,
                                    SqlTypeName = BuildSqlTypeName(schema, name),
                                    IsNullable = c.IsNullable,
                                    MaxLength = c.MaxLength,
                                    IsNestedJson = c.IsNestedJson,
                                    ReturnsJson = c.ReturnsJson,
                                    ReturnsJsonArray = c.ReturnsJsonArray,
                                    JsonRootProperty = c.JsonRootProperty,
                                    DeferredJsonExpansion = c.DeferredJsonExpansion
                                };

                                if (!isSystemType)
                                {
                                    column.UserTypeSchemaName = schema;
                                    column.UserTypeName = name;
                                }

                                if (c.Columns != null && c.Columns.Count > 0)
                                {
                                    column.Columns = c.Columns.Select(MapSnapshotColToRuntime).ToArray();
                                }

                                if (c.Reference != null)
                                {
                                    column.Reference = new StoredProcedureContentModel.ColumnReferenceInfo
                                    {
                                        Kind = c.Reference.Kind,
                                        Schema = c.Reference.Schema,
                                        Name = c.Reference.Name
                                    };
                                }

                                return column;
                            }
                            var rsModels = snapProc.ResultSets.Select(rs => new StoredProcedureContentModel.ResultSet
                            {
                                ReturnsJson = rs.ReturnsJson,
                                ReturnsJsonArray = rs.ReturnsJsonArray,
                                // removed flag
                                JsonRootProperty = rs.JsonRootProperty,
                                ExecSourceSchemaName = rs.ExecSourceSchemaName,
                                ExecSourceProcedureName = rs.ExecSourceProcedureName,
                                HasSelectStar = rs.HasSelectStar == true,
                                Columns = rs.Columns.Select(MapSnapshotColToRuntime).ToArray()
                            }).ToArray();
                            storedProcedure.Content = new StoredProcedureContentModel
                            {
                                Definition = storedProcedure.Content?.Definition,
                                Statements = storedProcedure.Content?.Statements ?? Array.Empty<string>(),
                                ContainsSelect = storedProcedure.Content?.ContainsSelect ?? true,
                                ContainsInsert = storedProcedure.Content?.ContainsInsert ?? false,
                                ContainsUpdate = storedProcedure.Content?.ContainsUpdate ?? false,
                                ContainsDelete = storedProcedure.Content?.ContainsDelete ?? false,
                                ContainsMerge = storedProcedure.Content?.ContainsMerge ?? false,
                                ContainsOpenJson = storedProcedure.Content?.ContainsOpenJson ?? false,
                                ResultSets = rsModels,
                                UsedFallbackParser = storedProcedure.Content?.UsedFallbackParser ?? false,
                                ParseErrorCount = storedProcedure.Content?.ParseErrorCount ?? 0,
                                FirstParseError = storedProcedure.Content?.FirstParseError,
                                ExecutedProcedures = storedProcedure.Content?.ExecutedProcedures
                            };
                        }
                        // Minimal input hydration if not present even after skip
                        if (storedProcedure.Input == null)
                        {
                            var inputs = await dbContext.StoredProcedureInputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                            storedProcedure.Input = inputs?.Select(i => new StoredProcedureInputModel(i)).ToList();
                        }
                    }
                }
                else
                {
                    // Full load & parse
                    var def = await dbContext.StoredProcedureDefinitionAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    var definition = def?.Definition;
                    storedProcedure.Content = StoredProcedureContentModel.Parse(definition, storedProcedure.SchemaName);
                    if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                    {
                        if (storedProcedure.Content?.UsedFallbackParser == true)
                        {
                            consoleService.Verbose($"[proc-parse-fallback] {storedProcedure.SchemaName}.{storedProcedure.Name} parse errors={storedProcedure.Content.ParseErrorCount} first='{storedProcedure.Content.FirstParseError}'");
                        }
                        else if (storedProcedure.Content?.ResultSets?.Count > 1)
                        {
                            consoleService.Verbose($"[proc-json-multi] {storedProcedure.SchemaName}.{storedProcedure.Name} sets={storedProcedure.Content.ResultSets.Count}");
                        }
                        else if (previousModifiedTicks.HasValue)
                        {
                            consoleService.Verbose($"[proc-loaded] {storedProcedure.SchemaName}.{storedProcedure.Name} updated {previousModifiedTicks.Value} -> {currentModifiedTicks}");
                        }
                        else
                        {
                            consoleService.Verbose($"[proc-loaded] {storedProcedure.SchemaName}.{storedProcedure.Name} initial load (ticks={currentModifiedTicks})");
                        }
                    }

                    // Inputs & Outputs
                    var inputsFull = await dbContext.StoredProcedureInputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    storedProcedure.Input = inputsFull?.Select(i => new StoredProcedureInputModel(i)).ToList();

                    var outputsFull = await dbContext.StoredProcedureOutputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    var outputModels = outputsFull?.Select(i => new StoredProcedureOutputModel(i)).ToList() ?? new List<StoredProcedureOutputModel>();
                    procedureOutputs[$"{storedProcedure.SchemaName}.{storedProcedure.Name}"] = outputModels;

                    // Synthesize ResultSet if no JSON sets and none parsed
                    var anyJson = storedProcedure.Content?.ResultSets?.Any(r => r.ReturnsJson) == true;
                    if (!anyJson && (storedProcedure.Content?.ResultSets == null || !storedProcedure.Content.ResultSets.Any()))
                    {
                        var syntheticColumns = outputModels.Select(o => new StoredProcedureContentModel.ResultColumn
                        {
                            Name = o.Name,
                            SqlTypeName = o.SqlTypeName,
                            IsNullable = o.IsNullable,
                            MaxLength = o.MaxLength
                        }).ToArray();
                        var syntheticSet = new StoredProcedureContentModel.ResultSet
                        {
                            ReturnsJson = false,
                            ReturnsJsonArray = false,
                            // removed flag
                            JsonRootProperty = null,
                            Columns = syntheticColumns
                        };
                        // Legacy FOR JSON single-column upgrade
                        if (syntheticSet.Columns.Count == 1 && string.Equals(syntheticSet.Columns[0].Name, "JSON_F52E2B61-18A1-11d1-B105-00805F49916B", StringComparison.OrdinalIgnoreCase) && (syntheticSet.Columns[0].SqlTypeName?.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase) ?? false))
                        {
                            if (Environment.GetEnvironmentVariable("SPOCR_JSON_LEGACY_SINGLE") == "1")
                            {
                                syntheticSet = new StoredProcedureContentModel.ResultSet
                                {
                                    ReturnsJson = true,
                                    ReturnsJsonArray = true,
                                    JsonRootProperty = null,
                                    Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                                };
                                if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                                    consoleService.Verbose($"[proc-json-legacy-upgrade] {storedProcedure.SchemaName}.{storedProcedure.Name} single synthetic FOR JSON column upgraded (flag)");
                            }
                            else if (ShouldDiagJsonMissAst())
                            {
                                consoleService.Output($"[proc-json-legacy-skip] {storedProcedure.SchemaName}.{storedProcedure.Name} legacy single-column FOR JSON sentinel ignored (flag disabled)");
                            }
                        }
                        storedProcedure.Content = new StoredProcedureContentModel
                        {
                            Definition = storedProcedure.Content?.Definition ?? definition,
                            Statements = storedProcedure.Content?.Statements ?? Array.Empty<string>(),
                            ContainsSelect = storedProcedure.Content?.ContainsSelect ?? false,
                            ContainsInsert = storedProcedure.Content?.ContainsInsert ?? false,
                            ContainsUpdate = storedProcedure.Content?.ContainsUpdate ?? false,
                            ContainsDelete = storedProcedure.Content?.ContainsDelete ?? false,
                            ContainsMerge = storedProcedure.Content?.ContainsMerge ?? false,
                            ContainsOpenJson = storedProcedure.Content?.ContainsOpenJson ?? false,
                            ResultSets = new[] { syntheticSet },
                            UsedFallbackParser = storedProcedure.Content?.UsedFallbackParser ?? false,
                            ParseErrorCount = storedProcedure.Content?.ParseErrorCount ?? 0,
                            FirstParseError = storedProcedure.Content?.FirstParseError,
                            ExecutedProcedures = storedProcedure.Content?.ExecutedProcedures
                        };
                    }
                }

                // Entfernt: Regex-basierte Prune/Recovery Logik. JSON-Erkennung rein AST-basiert (StoredProcedureContentModel.Parse).

                storedProcedure.ModifiedTicks = currentModifiedTicks;
                updatedSnapshot.Procedures.Add(new ProcedureCacheEntry
                {
                    Schema = storedProcedure.SchemaName,
                    Name = storedProcedure.Name,
                    ModifiedTicks = currentModifiedTicks
                });
            }

            // TableTypes per schema
            var tableTypeModels = new List<TableTypeModel>();
            foreach (var tableType in tableTypes.Where(tt => tt.SchemaName.Equals(schema.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var columns = await dbContext.TableTypeColumnListAsync(tableType.UserTypeId ?? -1, cancellationToken);
                tableTypeModels.Add(new TableTypeModel(tableType, columns));
            }
            schema.TableTypes = tableTypeModels;
        }

        // Vereinfachte Forwarding Normalisierung: Erzeuge ausschließlich Platzhalter für jede EXEC.
        // Keine Klonung oder Append der Ziel-ResultSets mehr; rekursive Expansion erfolgt erst im späteren Generierungsschritt.
        try
        {
            var allProcedures = schemas.SelectMany(s => s.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>()).ToList();
            foreach (var proc in allProcedures)
            {
                var c = proc.Content;
                if (c?.ExecutedProcedures == null || c.ExecutedProcedures.Count == 0) continue;
                var localSets = c.ResultSets?.Where(rs => string.IsNullOrEmpty(rs.ExecSourceProcedureName)).ToList() ?? new List<StoredProcedureContentModel.ResultSet>();
                var placeholders = c.ExecutedProcedures
                    .Where(e => e != null)
                    .Select(e =>
                    {
                        var target = allProcedures.FirstOrDefault(p =>
                            p.SchemaName.Equals(e.Schema, StringComparison.OrdinalIgnoreCase) &&
                            p.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase));
                        var forwardsJson = target?.Content?.ResultSets?.Any(rs => rs.ReturnsJson) == true;
                        var forwardsJsonArray = target?.Content?.ResultSets?.Any(rs => rs.ReturnsJsonArray) == true;
                        return new StoredProcedureContentModel.ResultSet
                        {
                            ExecSourceSchemaName = e.Schema,
                            ExecSourceProcedureName = e.Name,
                            ReturnsJson = forwardsJson,
                            ReturnsJsonArray = forwardsJsonArray,
                            Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                        };
                    }).ToList();
                // Reihenfolge aktuell: alle Platzhalter vor lokale Sets (vereinfachte Annahme); kann später durch Positionsdaten verfeinert werden.
                var augmentedLocalSets = localSets;
                if (localSets.Count > 0 && procedureOutputs.TryGetValue($"{proc.SchemaName}.{proc.Name}", out var outputs) && outputs.Count > 0)
                {
                    augmentedLocalSets = localSets.Select(rs =>
                    {
                        if (rs.Columns != null && rs.Columns.Count > 0) return rs;
                        var cols = outputs.Select(o => new StoredProcedureContentModel.ResultColumn
                        {
                            Name = o.Name,
                            SqlTypeName = o.SqlTypeName,
                            IsNullable = o.IsNullable,
                            MaxLength = o.MaxLength
                        }).ToArray();
                        return new StoredProcedureContentModel.ResultSet
                        {
                            ReturnsJson = rs.ReturnsJson,
                            ReturnsJsonArray = rs.ReturnsJsonArray,
                            JsonRootProperty = rs.JsonRootProperty,
                            ExecSourceSchemaName = rs.ExecSourceSchemaName,
                            ExecSourceProcedureName = rs.ExecSourceProcedureName,
                            HasSelectStar = rs.HasSelectStar,
                            Reference = rs.Reference,
                            Columns = cols
                        };
                    }).ToList();
                }

                var combined = new List<StoredProcedureContentModel.ResultSet>();
                combined.AddRange(placeholders);
                combined.AddRange(augmentedLocalSets);
                proc.Content = new StoredProcedureContentModel
                {
                    Definition = c.Definition,
                    Statements = c.Statements,
                    ContainsSelect = c.ContainsSelect,
                    ContainsInsert = c.ContainsInsert,
                    ContainsUpdate = c.ContainsUpdate,
                    ContainsDelete = c.ContainsDelete,
                    ContainsMerge = c.ContainsMerge,
                    ContainsOpenJson = c.ContainsOpenJson,
                    ResultSets = combined,
                    UsedFallbackParser = c.UsedFallbackParser,
                    ParseErrorCount = c.ParseErrorCount,
                    FirstParseError = c.FirstParseError,
                    ExecutedProcedures = c.ExecutedProcedures
                };
                consoleService.Verbose($"[proc-forward-placeholders] {proc.SchemaName}.{proc.Name} => placeholders={placeholders.Count} localSets={localSets.Count}");
            }
        }
        catch { /* best effort */ }

        if (totalSpCount > 0)
        {
            // Final completion already visually implied by 100% updates; CompleteProgress will emit separator + status.
            // Removed redundant DrawProgressBar(100) to avoid double rendering.
            consoleService.CompleteProgress(true, $"Loaded {totalSpCount} stored procedures");
        }

        // Persist updated cache (best-effort)
        var saveStart = DateTime.UtcNow;
        // (Reverted) Keine zusätzliche Stub-Erzeugung für ExecSource Ziele im Cache – ursprüngliche Logik wiederhergestellt.
        if (!disableCache)
        {
            // Sort cache entries for deterministic ordering (schema, procedure name)
            if (updatedSnapshot.Procedures.Count > 1)
            {
                updatedSnapshot.Procedures = updatedSnapshot.Procedures
                    .OrderBy(p => p.Schema, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            localCacheService?.Save(fingerprint, updatedSnapshot);
            consoleService.Verbose($"[cache] Saved snapshot {fingerprint} with {updatedSnapshot.Procedures.Count} entries in {(DateTime.UtcNow - saveStart).TotalMilliseconds:F1} ms");
        }
        else
        {
            consoleService.Verbose("[cache] Not saved (--no-cache)");
        }

        consoleService.Verbose($"[timing] Total schema load duration {(DateTime.UtcNow - loadStart).TotalMilliseconds:F1} ms");

        return schemas;
    }

    private static StoredProcedureInputModel? MapSnapshotInputToModel(SnapshotInput snapshotInput)
    {
        if (snapshotInput == null)
        {
            return null;
        }

        var stored = new StoredProcedureInput
        {
            Name = snapshotInput.Name ?? string.Empty,
            IsNullable = snapshotInput.IsNullable ?? false,
            MaxLength = snapshotInput.MaxLength ?? 0,
            IsOutput = snapshotInput.IsOutput ?? false,
            HasDefaultValue = snapshotInput.HasDefaultValue ?? false,
            Precision = snapshotInput.Precision,
            Scale = snapshotInput.Scale
        };

        var (schemaFromRef, nameFromRef) = SplitTypeRef(snapshotInput.TypeRef);

        var tableTypeSchema = snapshotInput.TableTypeSchema ?? schemaFromRef;
        var tableTypeName = snapshotInput.TableTypeName ?? nameFromRef;
        var scalarSchema = snapshotInput.TypeSchema ?? schemaFromRef;
        var scalarName = snapshotInput.TypeName ?? nameFromRef;

        var isTableType = !string.IsNullOrWhiteSpace(tableTypeSchema) && !string.IsNullOrWhiteSpace(tableTypeName);

        if (isTableType)
        {
            stored.IsTableType = true;
            stored.UserTypeSchemaName = tableTypeSchema;
            stored.UserTypeName = tableTypeName;
            stored.SqlTypeName = BuildSqlTypeName(tableTypeSchema, tableTypeName) ?? snapshotInput.TypeRef ?? string.Empty;
        }
        else
        {
            stored.IsTableType = false;

            if (!IsSystemSchema(scalarSchema) && !string.IsNullOrWhiteSpace(scalarName))
            {
                stored.UserTypeSchemaName = scalarSchema;
                stored.UserTypeName = scalarName;
            }

            stored.SqlTypeName = BuildSqlTypeName(scalarSchema, scalarName) ?? snapshotInput.TypeRef ?? string.Empty;
        }

        return new StoredProcedureInputModel(stored);
    }

    private static (string? Schema, string? Name) SplitTypeRef(string? typeRef)
    {
        if (string.IsNullOrWhiteSpace(typeRef))
        {
            return (null, null);
        }

        var parts = typeRef.Trim().Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            var schema = string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0];
            var name = string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1];
            return (schema, name);
        }

        var single = string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0];
        return (null, single);
    }

    private static string? BuildSqlTypeName(string? schema, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(schema) && !IsSystemSchema(schema))
        {
            return string.Concat(schema, ".", name);
        }

        return name;
    }

    private static bool IsSystemSchema(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return true;
        }

        return string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase);
    }
}

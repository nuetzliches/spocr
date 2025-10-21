using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext;
using SpocR.DataContext.Queries;
using SpocR.DataContext.Models;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SchemaManager(
    DbContext dbContext,
    IConsoleService consoleService,
    ISchemaSnapshotService schemaSnapshotService,
    Services.SchemaSnapshotFileLayoutService expandedSnapshotService,
    ILocalCacheService localCacheService = null
)
{
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
                var working = Utils.DirectoryUtils.GetWorkingDirectory();
                var schemaDir = System.IO.Path.Combine(working, ".spocr", "schema");
                // Fixup phase: repair procedures that have exactly one EXEC placeholder but are missing a local JSON result set.
                // Scenario: parser did not capture the FOR JSON SELECT (e.g. complex construction) and after forwarding only a placeholder remains.
                // Heuristic: If definition contains "FOR JSON" and ResultSets has exactly one placeholder (empty columns, ExecSource set, ReturnsJson=false) -> attempt reparse.
                // If reparse yields no JSON sets: add a minimal synthetic empty JSON set to preserve structure for downstream generation.
                try
                {
                    foreach (var schema in schemas)
                    {
                        foreach (var proc in schema.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>())
                        {
                            var content = proc.Content;
                            if (content?.ResultSets == null) continue;
                            if (content.ResultSets.Count != 1) continue; // Nur eindeutiges Problem-Muster
                            var rs0 = content.ResultSets[0];
                            bool isExecPlaceholderOnly = !rs0.ReturnsJson && !rs0.ReturnsJsonArray &&
                                                         !string.IsNullOrEmpty(rs0.ExecSourceProcedureName) && (rs0.Columns == null || rs0.Columns.Count == 0);
                            if (!isExecPlaceholderOnly) continue;
                            var def = content.Definition;
                            if (string.IsNullOrWhiteSpace(def))
                            {
                                try
                                {
                                    var defLoad = await dbContext.StoredProcedureDefinitionAsync(proc.SchemaName, proc.Name, cancellationToken);
                                    def = defLoad?.Definition;
                                }
                                catch { /* ignore */ }
                                if (string.IsNullOrWhiteSpace(def)) continue; // Kein Material verfügbar
                            }
                            if (def.IndexOf("FOR JSON", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue; // Keine JSON-Indikation
                            }
                            // Reparse zur Gewinnung potentieller JSON Sets
                            StoredProcedureContentModel reparsed = null;
                            try
                            {
                                reparsed = StoredProcedureContentModel.Parse(def, proc.SchemaName);
                            }
                            catch { /* best effort */ }
                            var jsonSets = reparsed?.ResultSets?.Where(r => r.ReturnsJson)?.ToList();
                            if (jsonSets != null && jsonSets.Count > 0)
                            {
                                // Platzhalter voranstellen, danach erkannte JSON Sets
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
                                consoleService.Verbose($"[proc-fixup-json-reparse] {proc.SchemaName}.{proc.Name} added {jsonSets.Count} JSON set(s) after placeholder-only detection");
                                continue;
                            }
                            // Add synthetic empty JSON set (minimal fallback)
                            var syntheticJson = new StoredProcedureContentModel.ResultSet
                            {
                                ReturnsJson = true,
                                ReturnsJsonArray = true,
                                // removed flag
                                JsonRootProperty = null,
                                Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                            };
                            // Attempt a simple column extraction from the SELECT preceding FOR JSON
                            try
                            {
                                var forJsonIdx = def.IndexOf("FOR JSON", StringComparison.OrdinalIgnoreCase);
                                if (forJsonIdx > 0)
                                {
                                    // Search backwards for the preceding SELECT
                                    var selectIdx = def.LastIndexOf("SELECT", forJsonIdx, StringComparison.OrdinalIgnoreCase);
                                    if (selectIdx >= 0 && selectIdx < forJsonIdx)
                                    {
                                        var selectSegment = def.Substring(selectIdx, forJsonIdx - selectIdx);
                                        // Remove initial SELECT keyword
                                        if (selectSegment.Length > 6)
                                        {
                                            selectSegment = selectSegment.Substring(6).Trim();
                                            // Remove possible TOP (...) segment (simplified)
                                            if (selectSegment.StartsWith("TOP", StringComparison.OrdinalIgnoreCase))
                                            {
                                                var afterTop = selectSegment.IndexOf(' ') + 1;
                                                if (afterTop > 0 && afterTop < selectSegment.Length)
                                                    selectSegment = selectSegment.Substring(afterTop).Trim();
                                            }
                                            // Primitive comma split (not handling nested expressions)
                                            var rawCols = selectSegment.Split(',');
                                            var colModels = new List<StoredProcedureContentModel.ResultColumn>();
                                            foreach (var raw in rawCols)
                                            {
                                                var part = raw.Trim();
                                                if (string.IsNullOrWhiteSpace(part)) continue;
                                                // Trim potential trailing FOR JSON remnants if split was imprecise
                                                var fjPos = part.IndexOf("FOR JSON", StringComparison.OrdinalIgnoreCase);
                                                if (fjPos >= 0) part = part.Substring(0, fjPos).Trim();
                                                // Extract alias (AS alias) or fall back to last token
                                                string name = null;
                                                var asIdx = part.LastIndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
                                                if (asIdx > -1 && asIdx + 4 < part.Length)
                                                {
                                                    name = part.Substring(asIdx + 4).Trim();
                                                }
                                                else
                                                {
                                                    // Split by spaces: take last token when not a function expression
                                                    var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                                    if (tokens.Length > 0)
                                                        name = tokens.Last();
                                                }
                                                if (string.IsNullOrWhiteSpace(name)) continue;
                                                // Remove brackets and schema/table prefixes
                                                name = name.Trim('[', ']', '"');
                                                if (name.Contains('.'))
                                                    name = name.Split('.').Last();
                                                // Ignore numeric tokens or parameters
                                                if (name.StartsWith("@")) continue;
                                                if (name.All(ch => char.IsDigit(ch))) continue;
                                                // JSON path defaults to name
                                                colModels.Add(new StoredProcedureContentModel.ResultColumn
                                                {
                                                    Name = name,
                                                    // JsonPath removed in flattened model
                                                    SqlTypeName = null, // Type unknown on purpose left null
                                                    IsNullable = true,
                                                    MaxLength = null
                                                });
                                            }
                                            if (colModels.Count > 0)
                                            {
                                                syntheticJson = new StoredProcedureContentModel.ResultSet
                                                {
                                                    ReturnsJson = true,
                                                    ReturnsJsonArray = true,
                                                    // removed flag
                                                    JsonRootProperty = null,
                                                    Columns = colModels.ToArray()
                                                };
                                                consoleService.Verbose($"[proc-fixup-json-cols] {proc.SchemaName}.{proc.Name} extracted {colModels.Count} column(s) for synthetic JSON set");
                                            }
                                        }
                                    }
                                }
                            }
                            catch { /* heuristic extraction best effort */ }
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
                                ResultSets = new[] { rs0, syntheticJson },
                                UsedFallbackParser = content.UsedFallbackParser,
                                ParseErrorCount = content.ParseErrorCount,
                                FirstParseError = content.FirstParseError,
                                ExecutedProcedures = content.ExecutedProcedures,
                                ContainsExecKeyword = content.ContainsExecKeyword,
                                RawExecCandidates = content.RawExecCandidates,
                                RawExecCandidateKinds = content.RawExecCandidateKinds
                            };
                            consoleService.Verbose($"[proc-fixup-json-synth] {proc.SchemaName}.{proc.Name} synthesized JSON set after placeholder-only detection");
                        }
                    }
                }
                catch (Exception jsonFixEx)
                {
                    consoleService.Verbose($"[proc-fixup-json-warn] {jsonFixEx.Message}");
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

        // Build a simple fingerprint (avoid secrets): use output namespace or role kind + schemas + SP count
        var projectId = config?.Project?.Output?.Namespace ?? config?.Project?.Role?.Kind.ToString() ?? "UnknownProject";
        var fingerprintRaw = $"{projectId}|{schemaListString}|{storedProcedures.Count}";
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprintRaw))).Substring(0, 16);

        var loadStart = DateTime.UtcNow;
        var disableCache = noCache;

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
                var working = Utils.DirectoryUtils.GetWorkingDirectory();
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
                            storedProcedure.Input = snapProc.Inputs.Select(i => new StoredProcedureInputModel(new DataContext.Models.StoredProcedureInput
                            {
                                Name = i.Name,
                                SqlTypeName = i.SqlTypeName,
                                IsNullable = i.IsNullable,
                                MaxLength = i.MaxLength,
                                IsOutput = i.IsOutput,
                                IsTableType = i.IsTableType,
                                UserTypeName = i.TableTypeName,
                                UserTypeSchemaName = i.TableTypeSchema
                            })).ToList();
                        }
                        // ResultSets hydration
                        if (snapProc.ResultSets?.Any() == true && (storedProcedure.Content?.ResultSets == null || !storedProcedure.Content.ResultSets.Any()))
                        {
                            StoredProcedureContentModel.ResultColumn MapSnapshotColToRuntime(SnapshotResultColumn c)
                            {
                                var rc = new StoredProcedureContentModel.ResultColumn
                                {
                                    Name = c.Name,
                                    SqlTypeName = c.SqlTypeName,
                                    IsNullable = c.IsNullable,
                                    MaxLength = c.MaxLength,
                                    UserTypeName = c.UserTypeName,
                                    UserTypeSchemaName = c.UserTypeSchemaName
                                };

                                return rc;
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
                                ParseErrorCount = storedProcedure.Content?.ParseErrorCount,
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
                            syntheticSet = new StoredProcedureContentModel.ResultSet
                            {
                                ReturnsJson = true,
                                ReturnsJsonArray = true,
                                // removed flag
                                JsonRootProperty = null,
                                Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                            };
                            if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                                consoleService.Verbose($"[proc-json-legacy-upgrade] {storedProcedure.SchemaName}.{storedProcedure.Name} single synthetic FOR JSON column upgraded to JSON.");
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
                            ParseErrorCount = storedProcedure.Content?.ParseErrorCount,
                            FirstParseError = storedProcedure.Content?.FirstParseError,
                            ExecutedProcedures = storedProcedure.Content?.ExecutedProcedures
                        };
                    }
                }

                // Prune pass (refined): remove only nested inline FOR JSON expression-derived result sets.
                // Detection now differentiates top-level FOR JSON output vs. inline column expression:
                //  - Top-level: last non-empty statement ends with FOR JSON PATH (optional , WITHOUT_ARRAY_WRAPPER) followed only by whitespace/semicolon/END
                //  - Nested: multiple FOR JSON occurrences or pattern inside parentheses ("(SELECT" ...) and NOT ending top-level.
                // We only prune the final JSON set if it's classified as nested.
                // After pruning, if NO JSON sets remain but a single top-level FOR JSON is detected, we recover a synthetic empty JSON set.
                try
                {
                    var contentCheck = storedProcedure.Content;
                    var rsListCheck = contentCheck?.ResultSets?.ToList();
                    var def = contentCheck?.Definition;
                    if (contentCheck != null && rsListCheck != null && rsListCheck.Count > 0 && !string.IsNullOrWhiteSpace(def))
                    {
                        var lastSet = rsListCheck.Last();
                        bool candidate = lastSet.ReturnsJson && (lastSet.Columns?.Count ?? 0) >= 1 && string.IsNullOrWhiteSpace(lastSet.JsonRootProperty);
                        if (candidate)
                        {
                            string defNorm = def.Replace('\r', ' ').Replace('\n', ' ').Trim();
                            // Remove trailing GO or END tokens for end-check
                            defNorm = System.Text.RegularExpressions.Regex.Replace(defNorm, @"(GO|END)\s*$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                            // Find last FOR JSON PATH occurrence
                            int lastForJsonPos = defNorm.LastIndexOf("FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                            bool hasForJson = lastForJsonPos >= 0;
                            bool hasWithoutWrapper = defNorm.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;
                            // Count occurrences
                            int forJsonCount = 0; int idxScan = 0;
                            while (idxScan < defNorm.Length)
                            {
                                int p = defNorm.IndexOf("FOR JSON PATH", idxScan, StringComparison.OrdinalIgnoreCase);
                                if (p < 0) break; forJsonCount++; idxScan = p + 12;
                            }
                            // Determine if last occurrence reaches end (allow trailing semicolon & whitespace)
                            bool endsTopLevel = false;
                            if (hasForJson)
                            {
                                string tail = defNorm.Substring(lastForJsonPos).ToUpperInvariant();
                                // Accept patterns ending with FOR JSON PATH or WITH WITHOUT_ARRAY_WRAPPER up to optional semicolon
                                endsTopLevel = System.Text.RegularExpressions.Regex.IsMatch(tail, @"^FOR JSON PATH(, WITHOUT_ARRAY_WRAPPER)?\s*;?\s*$");
                            }
                            bool hasNestedSelectPattern = defNorm.IndexOf("(SELECT", StringComparison.OrdinalIgnoreCase) >= 0;
                            bool multiNested = forJsonCount > 1 && !endsTopLevel;
                            bool isNested = hasForJson && !endsTopLevel && (hasNestedSelectPattern || multiNested);
                            if (isNested)
                            {
                                rsListCheck.RemoveAt(rsListCheck.Count - 1);
                                storedProcedure.Content = new StoredProcedureContentModel
                                {
                                    Definition = contentCheck.Definition,
                                    Statements = contentCheck.Statements,
                                    ContainsSelect = contentCheck.ContainsSelect,
                                    ContainsInsert = contentCheck.ContainsInsert,
                                    ContainsUpdate = contentCheck.ContainsUpdate,
                                    ContainsDelete = contentCheck.ContainsDelete,
                                    ContainsMerge = contentCheck.ContainsMerge,
                                    ContainsOpenJson = contentCheck.ContainsOpenJson,
                                    ResultSets = rsListCheck.ToArray(),
                                    UsedFallbackParser = contentCheck.UsedFallbackParser,
                                    ParseErrorCount = contentCheck.ParseErrorCount,
                                    FirstParseError = contentCheck.FirstParseError,
                                    ExecutedProcedures = contentCheck.ExecutedProcedures,
                                    ContainsExecKeyword = contentCheck.ContainsExecKeyword,
                                    RawExecCandidates = contentCheck.RawExecCandidates,
                                    RawExecCandidateKinds = contentCheck.RawExecCandidateKinds
                                };
                                consoleService.Verbose($"[proc-prune-json-nested] {storedProcedure.SchemaName}.{storedProcedure.Name} pruned nested inline FOR JSON (cols={lastSet.Columns?.Count})");
                            }
                            else
                            {
                                consoleService.Verbose($"[proc-prune-json-nested-skip] {storedProcedure.SchemaName}.{storedProcedure.Name} preserved top-level JSON output (nestedSelect={hasNestedSelectPattern} count={forJsonCount} endsTopLevel={endsTopLevel})");
                            }
                        }
                    }
                    // Recovery: if pruning removed all JSON sets incorrectly but a single top-level FOR JSON exists -> synthesize minimal JSON set.
                    if (storedProcedure.Content?.ResultSets != null)
                    {
                        var jsonCount = storedProcedure.Content.ResultSets.Count(r => r.ReturnsJson);
                        var defAll = storedProcedure.Content.Definition;
                        if (jsonCount == 0 && !string.IsNullOrWhiteSpace(defAll))
                        {
                            string defNorm2 = defAll.Replace('\r', ' ').Replace('\n', ' ').Trim();
                            defNorm2 = System.Text.RegularExpressions.Regex.Replace(defNorm2, @"(GO|END)\s*$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                            int lastPos = defNorm2.LastIndexOf("FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                            if (lastPos >= 0)
                            {
                                int occ = 0; int sIdx = 0; while (sIdx < defNorm2.Length) { var p2 = defNorm2.IndexOf("FOR JSON PATH", sIdx, StringComparison.OrdinalIgnoreCase); if (p2 < 0) break; occ++; sIdx = p2 + 12; }
                                string tail2 = defNorm2.Substring(lastPos).ToUpperInvariant();
                                bool endsTop = System.Text.RegularExpressions.Regex.IsMatch(tail2, @"^FOR JSON PATH(, WITHOUT_ARRAY_WRAPPER)?\s*;?\s*$");
                                if (occ == 1 && endsTop)
                                {
                                    // Synthesize empty array JSON result set
                                    var existing = storedProcedure.Content.ResultSets.ToList();
                                    existing.Add(new StoredProcedureContentModel.ResultSet
                                    {
                                        ReturnsJson = true,
                                        ReturnsJsonArray = true,
                                        // removed flag (implied by ReturnsJsonArray)
                                        JsonRootProperty = null,
                                        Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                                    });
                                    var old = storedProcedure.Content;
                                    storedProcedure.Content = new StoredProcedureContentModel
                                    {
                                        Definition = old.Definition,
                                        Statements = old.Statements,
                                        ContainsSelect = old.ContainsSelect,
                                        ContainsInsert = old.ContainsInsert,
                                        ContainsUpdate = old.ContainsUpdate,
                                        ContainsDelete = old.ContainsDelete,
                                        ContainsMerge = old.ContainsMerge,
                                        ContainsOpenJson = old.ContainsOpenJson,
                                        ResultSets = existing.ToArray(),
                                        UsedFallbackParser = old.UsedFallbackParser,
                                        ParseErrorCount = old.ParseErrorCount,
                                        FirstParseError = old.FirstParseError,
                                        ExecutedProcedures = old.ExecutedProcedures,
                                        ContainsExecKeyword = old.ContainsExecKeyword,
                                        RawExecCandidates = old.RawExecCandidates,
                                        RawExecCandidateKinds = old.RawExecCandidateKinds
                                    };
                                    consoleService.Verbose($"[proc-json-top-recover] {storedProcedure.SchemaName}.{storedProcedure.Name} recovered synthetic top-level JSON result set");
                                }
                            }
                        }
                    }
                }
                catch (Exception pruneEx)
                {
                    consoleService.Verbose($"[proc-prune-json-nested-warn] {storedProcedure.SchemaName}.{storedProcedure.Name} prune/refine failed: {pruneEx.Message}");
                }

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

        // Forwarding normalization: clone ResultSets from executed procedure for wrapper procs
        try
        {
            // Zweite Parse-Phase für Wrapper-Kandidaten ohne EXEC-Metadaten:
            // Falls eine Prozedur nur leere JSON Sets oder gar keine Sets besitzt UND keine ExecutedProcedures erfasst wurden,
            // versuchen wir erneut das Definition-Statement zu laden und zu parsen, um ExecuteSpecification zu erfassen.
            // Hintergrund: Skip-Pfad (canSkipDetails) kann zuvor Parsing übersprungen haben.
            foreach (var schema in schemas)
            {
                foreach (var proc in schema.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>())
                {
                    var content = proc.Content;
                    if (content == null) continue;
                    bool hasSets = content.ResultSets != null && content.ResultSets.Any();
                    bool onlyEmptyJsonSets = hasSets && content.ResultSets.All(rs => rs.ReturnsJson && (rs.Columns == null || rs.Columns.Count == 0));
                    bool execMissing = content.ExecutedProcedures == null || content.ExecutedProcedures.Count == 0;
                    bool wrapperCandidate = (!hasSets) || onlyEmptyJsonSets;
                    // Skip wrapper reparse if we previously skipped detailed parsing (cache hit) to avoid definition reload.
                    // Heuristic: ModifiedTicks equals current Modified.Ticks and execMissing implies we would trigger a reparse.
                    if (wrapperCandidate && execMissing)
                    {
                        try
                        {
                            var def = await dbContext.StoredProcedureDefinitionAsync(proc.SchemaName, proc.Name, cancellationToken);
                            var definition = def?.Definition;
                            if (!string.IsNullOrWhiteSpace(definition))
                            {
                                var reparsed = StoredProcedureContentModel.Parse(definition, proc.SchemaName);
                                // Nur übernehmen wenn jetzt genau ein EXEC gefunden wurde
                                if (reparsed.ExecutedProcedures != null && reparsed.ExecutedProcedures.Count == 1)
                                {
                                    proc.Content = new StoredProcedureContentModel
                                    {
                                        Definition = reparsed.Definition,
                                        Statements = reparsed.Statements,
                                        ContainsSelect = reparsed.ContainsSelect,
                                        ContainsInsert = reparsed.ContainsInsert,
                                        ContainsUpdate = reparsed.ContainsUpdate,
                                        ContainsDelete = reparsed.ContainsDelete,
                                        ContainsMerge = reparsed.ContainsMerge,
                                        ContainsOpenJson = reparsed.ContainsOpenJson,
                                        ResultSets = reparsed.ResultSets, // kann leer sein -> Forwarding übernimmt später
                                        UsedFallbackParser = reparsed.UsedFallbackParser,
                                        ParseErrorCount = reparsed.ParseErrorCount,
                                        FirstParseError = reparsed.FirstParseError,
                                        ExecutedProcedures = reparsed.ExecutedProcedures,
                                        ContainsExecKeyword = reparsed.ContainsExecKeyword,
                                        RawExecCandidates = reparsed.RawExecCandidates,
                                        RawExecCandidateKinds = reparsed.RawExecCandidateKinds
                                    };
                                    consoleService.Info($"[proc-reparse-wrapper] {proc.SchemaName}.{proc.Name} reparsed; execs=1 forwarding enabled");
                                }
                                else
                                {
                                    consoleService.Verbose($"[proc-reparse-wrapper-skip] {proc.SchemaName}.{proc.Name} reparsed execs={(reparsed.ExecutedProcedures == null ? "null" : reparsed.ExecutedProcedures.Count.ToString())}");
                                }
                            }
                        }
                        catch (Exception exReparse)
                        {
                            consoleService.Verbose($"[proc-reparse-wrapper-warn] {proc.SchemaName}.{proc.Name} {exReparse.Message}");
                        }
                    }
                }
            }

            var allProcedures = schemas.SelectMany(s => s.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>()).ToList();
            var procLookup = allProcedures
                .ToDictionary(p => ($"{p.SchemaName}.{p.Name}"), p => p, StringComparer.OrdinalIgnoreCase);

            foreach (var proc in allProcedures)
            {
                var content = proc.Content;
                if (content == null) continue;
                var hasSets = content.ResultSets != null && content.ResultSets.Any();
                bool onlyEmptyJsonSets = hasSets && content.ResultSets.All(rs => rs.ReturnsJson && (rs.Columns == null || rs.Columns.Count == 0));
                bool allNonJsonSets = hasSets && content.ResultSets.All(rs => !rs.ReturnsJson);
                bool hasOwnDml = content.ContainsSelect || content.ContainsInsert || content.ContainsUpdate || content.ContainsDelete || content.ContainsMerge;
                // Multi-EXEC Forwarding: mehr als ein inneres EXEC -> Erzeuge Platzhalter-Sets für alle Ziel-ResultSets (Reihenfolge: Wrapper lokale Sets vs. Forwarded?)
                if (content.ExecutedProcedures != null && content.ExecutedProcedures.Count > 1)
                {
                    var placeholders = new List<StoredProcedureContentModel.ResultSet>();
                    foreach (var execRef in content.ExecutedProcedures)
                    {
                        if (execRef == null) continue;
                        if (!procLookup.TryGetValue($"{execRef.Schema}.{execRef.Name}", out var targetProcMulti))
                        {
                            // Fallback: Snapshot oder direktes Laden ignorieren wir hier (Komplexität) – später erweiterbar.
                            // Minimaler Platzhalter, falls keine Zielsets bekannt.
                            placeholders.Add(new StoredProcedureContentModel.ResultSet
                            {
                                ExecSourceSchemaName = execRef.Schema,
                                ExecSourceProcedureName = execRef.Name,
                                Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                            });
                            continue;
                        }
                        var tSets = targetProcMulti.Content?.ResultSets ?? Array.Empty<StoredProcedureContentModel.ResultSet>();
                        if (tSets.Count == 0)
                        {
                            placeholders.Add(new StoredProcedureContentModel.ResultSet
                            {
                                ExecSourceSchemaName = execRef.Schema,
                                ExecSourceProcedureName = execRef.Name,
                                Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                            });
                        }
                        else
                        {
                            foreach (var ts in tSets)
                            {
                                placeholders.Add(new StoredProcedureContentModel.ResultSet
                                {
                                    ExecSourceSchemaName = execRef.Schema,
                                    ExecSourceProcedureName = execRef.Name,
                                    Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>() // Struktur wird nicht geklont; nur Verknüpfung
                                });
                            }
                        }
                    }
                    // Einfüge-Strategie: Erst alle Platzhalter (forwarded Reihenfolge innerer EXECs), dann lokale Sets damit Nummerierung konsistent bleibt.
                    var localSets = content.ResultSets?.ToList() ?? new List<StoredProcedureContentModel.ResultSet>();
                    bool hasAnyPlaceholder = localSets.Any(r => !string.IsNullOrEmpty(r.ExecSourceProcedureName) && (r.Columns == null || r.Columns.Count == 0));
                    if (!hasAnyPlaceholder)
                    {
                        var combinedOrdered = new List<StoredProcedureContentModel.ResultSet>();
                        combinedOrdered.AddRange(placeholders);
                        combinedOrdered.AddRange(localSets);
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
                            ResultSets = combinedOrdered,
                            UsedFallbackParser = content.UsedFallbackParser,
                            ParseErrorCount = content.ParseErrorCount,
                            FirstParseError = content.FirstParseError,
                            ExecutedProcedures = content.ExecutedProcedures
                        };
                        consoleService.Verbose($"[proc-forward-multi] {proc.SchemaName}.{proc.Name} added {placeholders.Count} placeholder set(s) for {content.ExecutedProcedures.Count} EXEC(s) (kept {localSets.Count} local set(s))");
                    }
                    continue; // Multi-EXEC spezifisch abgeschlossen
                }
                // Referenz-only Forwarding: Prozedur hat eigene JSON Sets und genau einen EXEC -> nur Platzhalter einfügen, keine Ziel-Sets klonen.
                if (content.ExecutedProcedures != null && content.ExecutedProcedures.Count == 1 && hasSets && content.ResultSets.Any(r => r.ReturnsJson))
                {
                    // Bereits vorhandener ExecSource Platzhalter?
                    bool hasPlaceholder = content.ResultSets.Any(r => !string.IsNullOrEmpty(r.ExecSourceProcedureName) && (r.Columns == null || r.Columns.Count == 0));
                    if (!hasPlaceholder)
                    {
                        var targetRef = content.ExecutedProcedures[0];
                        var placeholder = new StoredProcedureContentModel.ResultSet
                        {
                            ExecSourceSchemaName = targetRef.Schema,
                            ExecSourceProcedureName = targetRef.Name,
                            Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                        };
                        // Lokale Sets: sicherstellen, dass keine ExecSource* Felder gesetzt bleiben
                        var cleansedLocal = content.ResultSets.Select(ls =>
                        {
                            if (!string.IsNullOrEmpty(ls.ExecSourceProcedureName))
                            {
                                // Falls ein früherer Schritt fälschlich ExecSource gesetzt hat und Columns vorhanden sind -> entfernen
                                if (ls.Columns != null && ls.Columns.Count > 0)
                                {
                                    return new StoredProcedureContentModel.ResultSet
                                    {
                                        ReturnsJson = ls.ReturnsJson,
                                        ReturnsJsonArray = ls.ReturnsJsonArray,
                                        // removed flag
                                        JsonRootProperty = ls.JsonRootProperty,
                                        Columns = ls.Columns,
                                        HasSelectStar = ls.HasSelectStar
                                    };
                                }
                            }
                            return ls;
                        }).ToList();
                        var newSets = new List<StoredProcedureContentModel.ResultSet> { placeholder };
                        newSets.AddRange(cleansedLocal); // lokale Sets behalten (ohne ExecSource)
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
                            ExecutedProcedures = content.ExecutedProcedures
                        };
                        consoleService.Verbose($"[proc-forward-refonly] {proc.SchemaName}.{proc.Name} inserted ExecSource placeholder for {targetRef.Schema}.{targetRef.Name} (kept {content.ResultSets.Count} local set(s))");
                    }
                    // Keine vollständige Forwarding-Verarbeitung nötig
                    continue;
                }
                // Erweiterte Wrapper-Klassifikation: Auch Prozeduren mit ausschließlich non-JSON synthetischen Sets (kein eigener DML) gelten als Wrapper.
                bool isWrapperCandidate = (!hasSets) || onlyEmptyJsonSets || (allNonJsonSets && !hasOwnDml);
                if (!isWrapperCandidate)
                {
                    consoleService.Verbose($"[proc-forward-skip] {proc.SchemaName}.{proc.Name} classified as non-wrapper (hasSets={hasSets} onlyEmptyJsonSets={onlyEmptyJsonSets} allNonJsonSets={allNonJsonSets} hasOwnDml={hasOwnDml})");
                    continue; // hat eigene relevante Sets oder DML
                }
                if (content.ExecutedProcedures == null || content.ExecutedProcedures.Count != 1)
                {
                    // Unbedingtes Diagnose-Logging: EXEC vorhanden aber keine ExecutedProcedures erfasst
                    if ((content.ExecutedProcedures == null || content.ExecutedProcedures.Count == 0) && content.ContainsExecKeyword)
                    {
                        var candidates = (content.RawExecCandidates == null || content.RawExecCandidates.Count == 0)
                            ? "(keine Kandidaten extrahiert)"
                            : string.Join(", ", content.RawExecCandidates);
                        consoleService.Info($"[proc-exec-miss] {proc.SchemaName}.{proc.Name} EXEC erkannt aber kein AST-Knoten (candidates: {candidates})");
                    }
                    if (content.ExecutedProcedures == null || content.ExecutedProcedures.Count == 0)
                        consoleService.Verbose($"[proc-forward-skip] {proc.SchemaName}.{proc.Name} no executed procedures captured");
                    else
                        consoleService.Verbose($"[proc-forward-skip] {proc.SchemaName}.{proc.Name} multiple executed procedures ({content.ExecutedProcedures.Count})");
                    continue; // only single EXEC wrapper
                }
                // Bei eigener DML und nicht nur leeren JSON Sets wurde bereits vorher ausgeschlossen; kein erneutes Skip nötig.
                var target = content.ExecutedProcedures[0];
                if (target == null) continue;
                if (!procLookup.TryGetValue($"{target.Schema}.{target.Name}", out var targetProc))
                {
                    // Cross-Schema Fallback 1: Snapshot Hydration
                    StoredProcedureContentModel.ResultSet[] rsModels = Array.Empty<StoredProcedureContentModel.ResultSet>();
                    if (snapshotProcMap != null && snapshotProcMap.TryGetValue(target.Schema, out var map2) && map2.TryGetValue(target.Name, out var snapProc))
                    {
                        rsModels = snapProc.ResultSets.Select(rs => new StoredProcedureContentModel.ResultSet
                        {
                            ReturnsJson = rs.ReturnsJson,
                            ReturnsJsonArray = rs.ReturnsJsonArray,
                            // removed flag
                            JsonRootProperty = rs.JsonRootProperty,
                            Columns = rs.Columns.Select(c => new StoredProcedureContentModel.ResultColumn
                            {
                                Name = c.Name,
                                // JsonPath removed
                                SqlTypeName = c.SqlTypeName,
                                IsNullable = c.IsNullable,
                                MaxLength = c.MaxLength,
                                UserTypeName = c.UserTypeName,
                                UserTypeSchemaName = c.UserTypeSchemaName,
                                // Nested JsonResult handled via flattened properties (IsNestedJson/Columns) v7 rename
                            }).ToArray()
                        }).ToArray();
                    }
                    // Cross-Schema Fallback 2: Direkter DB Load (nur wenn Snapshot leer)
                    if (rsModels.Length == 0)
                    {
                        try
                        {
                            var def = await dbContext.StoredProcedureDefinitionAsync(target.Schema, target.Name, cancellationToken);
                            var definition = def?.Definition;
                            StoredProcedureContentModel parsed = null;
                            if (!string.IsNullOrWhiteSpace(definition))
                            {
                                parsed = StoredProcedureContentModel.Parse(definition, target.Schema);
                            }
                            var output = await dbContext.StoredProcedureOutputListAsync(target.Schema, target.Name, cancellationToken);
                            var outputModels = output?.Select(i => new StoredProcedureOutputModel(i)).ToList() ?? new List<StoredProcedureOutputModel>();
                            var anyJson = parsed?.ResultSets?.Any(r => r.ReturnsJson) == true;
                            var synthSets = new List<StoredProcedureContentModel.ResultSet>();
                            if (parsed?.ResultSets != null && parsed.ResultSets.Any())
                            {
                                synthSets.AddRange(parsed.ResultSets);
                            }
                            // Falls keine JSON Sets: klassisches Output zu synthetischem Set
                            if (!anyJson && synthSets.Count == 0 && outputModels.Any())
                            {
                                var syntheticColumns = outputModels.Select(o => new StoredProcedureContentModel.ResultColumn
                                {
                                    Name = o.Name,
                                    SqlTypeName = o.SqlTypeName,
                                    IsNullable = o.IsNullable,
                                    MaxLength = o.MaxLength
                                }).ToArray();
                                synthSets.Add(new StoredProcedureContentModel.ResultSet
                                {
                                    ReturnsJson = false,
                                    ReturnsJsonArray = false,
                                    // removed flag
                                    JsonRootProperty = null,
                                    Columns = syntheticColumns
                                });
                            }
                            rsModels = synthSets.ToArray();
                            if (rsModels.Length > 0)
                            {
                                consoleService.Verbose($"[proc-forward-xschema-load] {proc.SchemaName}.{proc.Name} loaded target {target.Schema}.{target.Name} directly from DB (sets={rsModels.Length})");
                            }
                        }
                        catch (Exception exLoad)
                        {
                            consoleService.Verbose($"[proc-forward-xschema-load-warn] {proc.SchemaName}.{proc.Name} direct load failed {target.Schema}.{target.Name}: {exLoad.Message}");
                        }
                    }
                    if (rsModels.Length > 0)
                    {
                        // Cross-Schema Ziel (ignoriertes Schema) für Cache persistieren, damit Banking etc. im Snapshot erscheint
                        var clonedSetsXs = rsModels.Select(rs => new StoredProcedureContentModel.ResultSet
                        {
                            ReturnsJson = rs.ReturnsJson,
                            ReturnsJsonArray = rs.ReturnsJsonArray,
                            // removed flag
                            JsonRootProperty = rs.JsonRootProperty,
                            ExecSourceSchemaName = target.Schema,
                            ExecSourceProcedureName = target.Name,
                            Columns = rs.Columns
                        }).ToArray();
                        proc.Content = new StoredProcedureContentModel
                        {
                            Definition = proc.Content.Definition,
                            Statements = proc.Content.Statements,
                            ContainsSelect = proc.Content.ContainsSelect,
                            ContainsInsert = proc.Content.ContainsInsert,
                            ContainsUpdate = proc.Content.ContainsUpdate,
                            ContainsDelete = proc.Content.ContainsDelete,
                            ContainsMerge = proc.Content.ContainsMerge,
                            ContainsOpenJson = proc.Content.ContainsOpenJson,
                            ResultSets = clonedSetsXs,
                            UsedFallbackParser = proc.Content.UsedFallbackParser,
                            ParseErrorCount = proc.Content.ParseErrorCount,
                            FirstParseError = proc.Content.FirstParseError,
                            ExecutedProcedures = proc.Content.ExecutedProcedures
                        };
                        consoleService.Verbose($"[proc-forward-xschema{(onlyEmptyJsonSets ? "-upgrade" : string.Empty)}] {proc.SchemaName}.{proc.Name} forwarded {clonedSetsXs.Length} set(s) from {(snapshotProcMap != null && snapshotProcMap.TryGetValue(target.Schema, out var _) ? "snapshot" : "direct-load")} {target.Schema}.{target.Name}");
                    }
                    continue;
                }
                var targetSets = targetProc.Content?.ResultSets;
                if (targetSets == null || !targetSets.Any()) continue;

                // Clone target sets
                var clonedSets = targetSets.Select(rs => new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = rs.ReturnsJson,
                    ReturnsJsonArray = rs.ReturnsJsonArray,
                    // removed flag
                    JsonRootProperty = rs.JsonRootProperty,
                    ExecSourceSchemaName = target.Schema,
                    ExecSourceProcedureName = target.Name,
                    Columns = rs.Columns.Select(c => new StoredProcedureContentModel.ResultColumn
                    {
                        Name = c.Name,
                        // JsonPath removed
                        SourceSchema = c.SourceSchema,
                        SourceTable = c.SourceTable,
                        SourceColumn = c.SourceColumn,
                        SqlTypeName = c.SqlTypeName,
                        IsNullable = c.IsNullable,
                        MaxLength = c.MaxLength,
                        SourceAlias = c.SourceAlias,
                        ExpressionKind = c.ExpressionKind,
                        IsNestedJson = c.IsNestedJson,
                        ForcedNullable = c.ForcedNullable,
                        IsAmbiguous = c.IsAmbiguous,
                        CastTargetType = c.CastTargetType,
                        UserTypeName = c.UserTypeName,
                        UserTypeSchemaName = c.UserTypeSchemaName,
                        // JsonResult removed
                    }).ToArray()
                }).ToArray();
                // Optional: spezifische Diagnose kann hier per Konfiguration ergänzt werden (keine Namensheuristik mehr)

                // Nur ExecSource Platzhalter ersetzen: bestehende Sets, die keine echten Inhalte haben (Columns leer, kein JSON) und bereits ExecSource referenzieren
                var existingSets = content.ResultSets ?? new List<StoredProcedureContentModel.ResultSet>();
                bool isPlaceholder(StoredProcedureContentModel.ResultSet rs) =>
                    (rs.Columns == null || rs.Columns.Count == 0) && !rs.ReturnsJson &&
                    !string.IsNullOrEmpty(rs.ExecSourceProcedureName);
                var preserved = existingSets.Where(rs => !isPlaceholder(rs)).ToList();
                // Falls alle Sets Platzhalter waren (klassischer Wrapper) -> vollständiger Ersatz
                List<StoredProcedureContentModel.ResultSet> final;
                if (preserved.Count == 0)
                {
                    final = clonedSets.ToList();
                    consoleService.Verbose($"[proc-forward-replace{(onlyEmptyJsonSets ? "-upgrade" : string.Empty)}] {proc.SchemaName}.{proc.Name} replaced placeholder sets with {clonedSets.Length} forwarded set(s) from {target.Schema}.{target.Name}");
                }
                else
                {
                    // Append forwarded Sets hinter eigene Sets
                    final = preserved.Concat(clonedSets).ToList();
                    consoleService.Verbose($"[proc-forward-append] {proc.SchemaName}.{proc.Name} appended {clonedSets.Length} forwarded set(s) (kept {preserved.Count} local set(s)) from {target.Schema}.{target.Name}");
                }
                proc.Content = new StoredProcedureContentModel
                {
                    Definition = proc.Content.Definition,
                    Statements = proc.Content.Statements,
                    ContainsSelect = proc.Content.ContainsSelect,
                    ContainsInsert = proc.Content.ContainsInsert,
                    ContainsUpdate = proc.Content.ContainsUpdate,
                    ContainsDelete = proc.Content.ContainsDelete,
                    ContainsMerge = proc.Content.ContainsMerge,
                    ContainsOpenJson = proc.Content.ContainsOpenJson,
                    ResultSets = final,
                    UsedFallbackParser = proc.Content.UsedFallbackParser,
                    ParseErrorCount = proc.Content.ParseErrorCount,
                    FirstParseError = proc.Content.FirstParseError,
                    ExecutedProcedures = proc.Content.ExecutedProcedures
                };
            }
        }
        catch { /* best effort */ }
        // EXEC append (non-wrapper) normalization: append target sets when caller has its own meaningful sets and exactly one EXEC.
        try
        {
            var allProcedures2 = schemas.SelectMany(s => s.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>()).ToList();
            var procLookup2 = allProcedures2.ToDictionary(p => ($"{p.SchemaName}.{p.Name}"), p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var sp in allProcedures2)
            {
                var content = sp.Content;
                if (content?.ExecutedProcedures == null || content.ExecutedProcedures.Count != 1) continue;
                if (content.ResultSets == null || !content.ResultSets.Any()) continue; // wrapper case handled earlier
                if (content.ResultSets.Any(r => !string.IsNullOrEmpty(r.ExecSourceProcedureName))) continue; // already forwarded/appended
                var target = content.ExecutedProcedures[0];
                if (!procLookup2.TryGetValue($"{target.Schema}.{target.Name}", out var targetProc))
                {
                    // Cross-Schema Append Fallback 1: Snapshot
                    StoredProcedureContentModel.ResultSet[] rsModels = Array.Empty<StoredProcedureContentModel.ResultSet>();
                    if (snapshotProcMap != null && snapshotProcMap.TryGetValue(target.Schema, out var map3) && map3.TryGetValue(target.Name, out var snapProc))
                    {
                        rsModels = snapProc.ResultSets.Select(rs => new StoredProcedureContentModel.ResultSet
                        {
                            ReturnsJson = rs.ReturnsJson,
                            ReturnsJsonArray = rs.ReturnsJsonArray,
                            // removed flag
                            JsonRootProperty = rs.JsonRootProperty,
                            Columns = rs.Columns.Select(c => new StoredProcedureContentModel.ResultColumn
                            {
                                Name = c.Name,
                                // JsonPath removed
                                SqlTypeName = c.SqlTypeName,
                                IsNullable = c.IsNullable,
                                MaxLength = c.MaxLength,
                                UserTypeName = c.UserTypeName,
                                UserTypeSchemaName = c.UserTypeSchemaName
                            }).ToArray()
                        }).ToArray();
                    }
                    // Cross-Schema Append Fallback 2: Direkt DB Load
                    if (rsModels.Length == 0)
                    {
                        try
                        {
                            var def = await dbContext.StoredProcedureDefinitionAsync(target.Schema, target.Name, cancellationToken);
                            var definition = def?.Definition;
                            StoredProcedureContentModel parsed = null;
                            if (!string.IsNullOrWhiteSpace(definition))
                                parsed = StoredProcedureContentModel.Parse(definition, target.Schema);
                            var output = await dbContext.StoredProcedureOutputListAsync(target.Schema, target.Name, cancellationToken);
                            var outputModels = output?.Select(i => new StoredProcedureOutputModel(i)).ToList() ?? new List<StoredProcedureOutputModel>();
                            var anyJson = parsed?.ResultSets?.Any(r => r.ReturnsJson) == true;
                            var synthSets = new List<StoredProcedureContentModel.ResultSet>();
                            if (parsed?.ResultSets != null && parsed.ResultSets.Any()) synthSets.AddRange(parsed.ResultSets);
                            if (!anyJson && synthSets.Count == 0 && outputModels.Any())
                            {
                                var syntheticColumns = outputModels.Select(o => new StoredProcedureContentModel.ResultColumn
                                {
                                    Name = o.Name,
                                    SqlTypeName = o.SqlTypeName,
                                    IsNullable = o.IsNullable,
                                    MaxLength = o.MaxLength
                                }).ToArray();
                                synthSets.Add(new StoredProcedureContentModel.ResultSet
                                {
                                    ReturnsJson = false,
                                    ReturnsJsonArray = false,
                                    // removed flag
                                    JsonRootProperty = null,
                                    Columns = syntheticColumns
                                });
                            }
                            rsModels = synthSets.ToArray();
                            if (rsModels.Length > 0)
                                consoleService.Verbose($"[proc-exec-append-xschema-load] {sp.SchemaName}.{sp.Name} loaded target {target.Schema}.{target.Name} directly from DB (sets={rsModels.Length})");
                        }
                        catch (Exception exLoad2)
                        {
                            consoleService.Verbose($"[proc-exec-append-xschema-load-warn] {sp.SchemaName}.{sp.Name} direct load failed {target.Schema}.{target.Name}: {exLoad2.Message}");
                        }
                    }
                    if (rsModels.Length > 0)
                    {
                        // Cross-Schema Append Ziel (ignoriertes Schema) für Cache persistieren
                        var appendedXs = rsModels.Select(rs => new StoredProcedureContentModel.ResultSet
                        {
                            ReturnsJson = rs.ReturnsJson,
                            ReturnsJsonArray = rs.ReturnsJsonArray,
                            // removed flag
                            JsonRootProperty = rs.JsonRootProperty,
                            ExecSourceSchemaName = target.Schema,
                            ExecSourceProcedureName = target.Name,
                            Columns = rs.Columns
                        }).ToArray();
                        var combinedXs = content.ResultSets.Concat(appendedXs).ToList();
                        sp.Content = new StoredProcedureContentModel
                        {
                            Definition = content.Definition,
                            Statements = content.Statements,
                            ContainsSelect = content.ContainsSelect,
                            ContainsInsert = content.ContainsInsert,
                            ContainsUpdate = content.ContainsUpdate,
                            ContainsDelete = content.ContainsDelete,
                            ContainsMerge = content.ContainsMerge,
                            ContainsOpenJson = content.ContainsOpenJson,
                            ResultSets = combinedXs,
                            UsedFallbackParser = content.UsedFallbackParser,
                            ParseErrorCount = content.ParseErrorCount,
                            FirstParseError = content.FirstParseError,
                            ExecutedProcedures = content.ExecutedProcedures
                        };
                        consoleService.Verbose($"[proc-exec-append-xschema] {sp.SchemaName}.{sp.Name} appended {appendedXs.Length} set(s) from {(snapshotProcMap != null && snapshotProcMap.TryGetValue(target.Schema, out var _) ? "snapshot" : "direct-load")} {target.Schema}.{target.Name}");
                    }
                    continue;
                }
                var targetSetsLocal = targetProc.Content?.ResultSets;
                if (targetSetsLocal == null || !targetSetsLocal.Any()) continue;
                // Append clones
                var appended = targetSetsLocal.Select(rs => new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = rs.ReturnsJson,
                    ReturnsJsonArray = rs.ReturnsJsonArray,
                    // removed flag
                    JsonRootProperty = rs.JsonRootProperty,
                    ExecSourceSchemaName = target.Schema,
                    ExecSourceProcedureName = target.Name,
                    HasSelectStar = rs.HasSelectStar,
                    Columns = rs.Columns.Select(c => new StoredProcedureContentModel.ResultColumn
                    {
                        Name = c.Name,
                        // JsonPath removed
                        SourceSchema = c.SourceSchema,
                        SourceTable = c.SourceTable,
                        SourceColumn = c.SourceColumn,
                        SqlTypeName = c.SqlTypeName,
                        IsNullable = c.IsNullable,
                        MaxLength = c.MaxLength,
                        SourceAlias = c.SourceAlias,
                        ExpressionKind = c.ExpressionKind,
                        IsNestedJson = c.IsNestedJson,
                        ForcedNullable = c.ForcedNullable,
                        IsAmbiguous = c.IsAmbiguous,
                        CastTargetType = c.CastTargetType,
                        UserTypeName = c.UserTypeName,
                        UserTypeSchemaName = c.UserTypeSchemaName,
                        // JsonResult removed
                    }).ToArray()
                }).ToArray();
                // Immer Append bei nicht-Wrapper (Namensheuristik entfernt)
                var finalSets = content.ResultSets.Concat(appended).ToList();
                sp.Content = new StoredProcedureContentModel
                {
                    Definition = content.Definition,
                    Statements = content.Statements,
                    ContainsSelect = content.ContainsSelect,
                    ContainsInsert = content.ContainsInsert,
                    ContainsUpdate = content.ContainsUpdate,
                    ContainsDelete = content.ContainsDelete,
                    ContainsMerge = content.ContainsMerge,
                    ContainsOpenJson = content.ContainsOpenJson,
                    ResultSets = finalSets,
                    UsedFallbackParser = content.UsedFallbackParser,
                    ParseErrorCount = content.ParseErrorCount,
                    FirstParseError = content.FirstParseError,
                    ExecutedProcedures = content.ExecutedProcedures
                };
                consoleService.Verbose($"[proc-exec-append] {sp.SchemaName}.{sp.Name} appended {appended.Count()} set(s) from {target.Schema}.{target.Name}");
            }
        }
        catch { /* best effort */ }

        // Post-processing: Wrapper reclassification + ResultSet de-duplication
        try
        {
            bool IsPureWrapper(StoredProcedureModel sp, IDictionary<string, StoredProcedureModel> lookup)
            {
                var c = sp.Content;
                if (c?.ExecutedProcedures == null || c.ExecutedProcedures.Count != 1) return false;
                if (c.ContainsInsert || c.ContainsUpdate || c.ContainsDelete || c.ContainsMerge) return false;
                var targetRef = c.ExecutedProcedures[0];
                if (!lookup.TryGetValue($"{targetRef.Schema}.{targetRef.Name}", out var targetSp)) return false;
                var targetSets = targetSp.Content?.ResultSets;
                var ownSets = c.ResultSets?.ToList() ?? new List<StoredProcedureContentModel.ResultSet>();
                if (ownSets.Count == 0) return true;
                // NEU: Wenn es ein eigenes (nicht forwardetes) JSON ResultSet mit echten Columns gibt, ist es kein reiner Wrapper.
                bool hasOwnConcreteJson = ownSets.Any(rs => string.IsNullOrEmpty(rs.ExecSourceProcedureName) && rs.ReturnsJson && (rs.Columns?.Count ?? 0) > 0);
                if (hasOwnConcreteJson) return false;
                if (targetSets == null || targetSets.Count == 0) return false;
                foreach (var os in ownSets)
                {
                    bool subset = targetSets.Any(ts => (os.Columns ?? Array.Empty<StoredProcedureContentModel.ResultColumn>())
                        .All(col => (ts.Columns ?? Array.Empty<StoredProcedureContentModel.ResultColumn>())
                            .Any(tc => tc.Name.Equals(col.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(tc.SqlTypeName, col.SqlTypeName, StringComparison.OrdinalIgnoreCase))));
                    if (!subset) return false;
                }
                return true;
            }

            var allProcedures3 = schemas.SelectMany(s => s.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>()).ToList();
            var procLookup3 = allProcedures3.ToDictionary(p => ($"{p.SchemaName}.{p.Name}"), p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var sp in allProcedures3)
            {
                var c = sp.Content;
                if (c == null) continue;
                if (IsPureWrapper(sp, procLookup3))
                {
                    var targetRef = c.ExecutedProcedures[0];
                    if (procLookup3.TryGetValue($"{targetRef.Schema}.{targetRef.Name}", out var targetSp))
                    {
                        // Keep exactly ONE placeholder/reference ResultSet instead of cloning all target sets.
                        // This placeholder carries ExecSource* metadata so that later generation phases can expand
                        // to the target procedure's own ResultSets (0..n) without premature column cloning.
                        // Create a fresh placeholder result set carrying only ExecSource metadata.
                        var refSet = new StoredProcedureContentModel.ResultSet
                        {
                            ExecSourceProcedureName = targetSp.Name,
                            ExecSourceSchemaName = targetSp.SchemaName,
                            Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                        };

                        var refList = new List<StoredProcedureContentModel.ResultSet> { refSet };
                        sp.Content = new StoredProcedureContentModel
                        {
                            Definition = c.Definition,
                            Statements = c.Statements,
                            ContainsSelect = c.ContainsSelect,
                            ContainsInsert = c.ContainsInsert,
                            ContainsUpdate = c.ContainsUpdate,
                            ContainsDelete = c.ContainsDelete,
                            ContainsMerge = c.ContainsMerge,
                            ContainsOpenJson = c.ContainsOpenJson,
                            ResultSets = refList,
                            UsedFallbackParser = c.UsedFallbackParser,
                            ParseErrorCount = c.ParseErrorCount,
                            FirstParseError = c.FirstParseError,
                            ExecutedProcedures = c.ExecutedProcedures
                        };
                        c = sp.Content;
                        consoleService.Verbose($"[proc-wrapper-posthoc] {sp.SchemaName}.{sp.Name} normalized to single ExecSource placeholder -> {targetSp.SchemaName}.{targetSp.Name}.");
                    }
                }
                var rsList = c.ResultSets;
                if (rsList != null && rsList.Count > 1)
                {
                    var distinct = rsList.GroupBy(rs => string.Join("|", new[] {
                            rs.ExecSourceProcedureName ?? string.Empty,
                            rs.JsonRootProperty ?? string.Empty,
                            rs.ReturnsJson.ToString(),
                            rs.ReturnsJsonArray.ToString(),
                            // removed flag placeholder
                            string.Join(",", rs.Columns?.Select(col => col.Name+":"+col.SqlTypeName+":"+col.IsNullable) ?? Enumerable.Empty<string>())
                        }))
                        .Select(g => g.First())
                        .ToList();
                    if (distinct.Count != rsList.Count)
                    {
                        // Recreate content model to satisfy init-only property
                        var old = c;
                        sp.Content = new StoredProcedureContentModel
                        {
                            Definition = old.Definition,
                            Statements = old.Statements,
                            ContainsSelect = old.ContainsSelect,
                            ContainsInsert = old.ContainsInsert,
                            ContainsUpdate = old.ContainsUpdate,
                            ContainsDelete = old.ContainsDelete,
                            ContainsMerge = old.ContainsMerge,
                            ContainsOpenJson = old.ContainsOpenJson,
                            ResultSets = distinct,
                            UsedFallbackParser = old.UsedFallbackParser,
                            ParseErrorCount = old.ParseErrorCount,
                            FirstParseError = old.FirstParseError,
                            ExecutedProcedures = old.ExecutedProcedures
                        };
                        c = sp.Content;
                        consoleService.Verbose($"[proc-dedupe] {sp.SchemaName}.{sp.Name} removed {rsList.Count - distinct.Count} duplicate result set(s).");
                    }
                }
            }
        }
        catch (Exception dedupeEx)
        {
            consoleService.Verbose($"[proc-dedupe-warn] {dedupeEx.Message}");
        }

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
}

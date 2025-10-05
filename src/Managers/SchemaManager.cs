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
    ILocalCacheService localCacheService = null
)
{
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
            // Snapshot-only mode (legacy schema node removed). Apply default + IgnoredSchemas list.
            var ignored = config.Project.IgnoredSchemas ?? new List<string>();
            foreach (var schema in schemas)
            {
                schema.Status = config.Project.DefaultSchemaStatus;
                if (ignored.Contains(schema.Name, StringComparer.OrdinalIgnoreCase))
                {
                    schema.Status = SchemaStatusEnum.Ignore;
                }
            }
            if (ignored.Count > 0)
            {
                consoleService.Verbose($"[ignore] Applied IgnoredSchemas list ({ignored.Count})");
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

        // reorder schemas, ignored at top
        schemas = schemas.OrderByDescending(schema => schema.Status).ToList();

        var activeSchemas = schemas.Where(i => i.Status != SchemaStatusEnum.Ignore).ToList();
        if (!activeSchemas.Any())
        {
            // Fallback: if there are stored procedures later that reference schemas we ignored entirely due to config, we would miss them.
            // To keep behavior stable for tests and migration, emit warning and return early.
            consoleService.Warn("No schemas found or all schemas ignored!");
            return schemas;
        }
        var schemaListString = string.Join(',', activeSchemas.Select(i => $"'{i.Name}'"));

        var storedProcedures = await dbContext.StoredProcedureListAsync(schemaListString, cancellationToken);

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

        // NOTE: Current Modified Ticks werden aus dem sys.objects modify_date geladen (siehe StoredProcedure.Modified)

        // Build snapshot procedure lookup (latest snapshot) for hydration of skipped procedures
        Dictionary<string, Dictionary<string, SnapshotProcedure>> snapshotProcMap = null;
        try
        {
            if (!disableCache) // only attempt if not a forced full refresh
            {
                var working = Utils.DirectoryUtils.GetWorkingDirectory();
                var schemaDir = System.IO.Path.Combine(working, ".spocr", "schema");
                if (System.IO.Directory.Exists(schemaDir))
                {
                    var latest = System.IO.Directory.GetFiles(schemaDir, "*.json")
                        .Select(f => new System.IO.FileInfo(f))
                        .OrderByDescending(fi => fi.LastWriteTimeUtc)
                        .FirstOrDefault();
                    if (latest != null)
                    {
                        var snap = schemaSnapshotService.Load(System.IO.Path.GetFileNameWithoutExtension(latest.Name));
                        if (snap?.Procedures?.Any() == true)
                        {
                            snapshotProcMap = snap.Procedures
                                .GroupBy(p => p.Schema, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
                            consoleService.Verbose($"[snapshot-hydrate] Loaded previous snapshot for hydration (fingerprint={snap.Fingerprint})");
                        }
                    }
                }
            }
        }
        catch { /* best effort, ignore */ }

        foreach (var schema in schemas)
        {
            schema.StoredProcedures = storedProcedures.Where(i => i.SchemaName.Equals(schema.Name))?.Select(i => new StoredProcedureModel(i))?.ToList();

            if (schema.StoredProcedures == null)
                continue;

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

                // Current modify_date from DB list (DateTime) -> ticks
                var currentModifiedTicks = storedProcedure.Modified.Ticks;
                var cacheEntry = cache?.Procedures.FirstOrDefault(p => p.Schema == storedProcedure.SchemaName && p.Name == storedProcedure.Name);
                var previousModifiedTicks = cacheEntry?.ModifiedTicks;
                var canSkipDetails = !disableCache && previousModifiedTicks.HasValue && previousModifiedTicks.Value == currentModifiedTicks;
                if (canSkipDetails)
                {
                    consoleService.Verbose($"[proc-skip] {storedProcedure.SchemaName}.{storedProcedure.Name} unchanged (ticks={currentModifiedTicks})");
                    // Hydrate minimal metadata from last snapshot (faster & canonical) instead of cache extended metadata
                    if (snapshotProcMap != null && snapshotProcMap.TryGetValue(storedProcedure.SchemaName, out var spMap) && spMap.TryGetValue(storedProcedure.Name, out var snapProc))
                    {
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
                        if (snapProc.ResultSets?.Any() == true && (storedProcedure.Content?.ResultSets == null || !storedProcedure.Content.ResultSets.Any()))
                        {
                            var rsModels = snapProc.ResultSets.Select(rs => new StoredProcedureContentModel.ResultSet
                            {
                                ReturnsJson = rs.ReturnsJson,
                                ReturnsJsonArray = rs.ReturnsJsonArray,
                                ReturnsJsonWithoutArrayWrapper = rs.ReturnsJsonWithoutArrayWrapper,
                                JsonRootProperty = rs.JsonRootProperty,
                                Columns = rs.Columns.Select(c => new StoredProcedureContentModel.ResultColumn
                                {
                                    Name = c.Name,
                                    SqlTypeName = c.SqlTypeName,
                                    IsNullable = c.IsNullable,
                                    MaxLength = c.MaxLength
                                }).ToArray()
                            }).ToArray();
                            storedProcedure.Content = new StoredProcedureContentModel
                            {
                                Definition = null,
                                Statements = Array.Empty<string>(),
                                ContainsSelect = true,
                                ResultSets = rsModels
                            };
                        }
                    }
                }
                else if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed && previousModifiedTicks.HasValue)
                {
                    consoleService.Verbose($"[proc-loaded] {storedProcedure.SchemaName}.{storedProcedure.Name} updated {previousModifiedTicks.Value} -> {currentModifiedTicks}");
                }
                else if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                {
                    consoleService.Verbose($"[proc-loaded] {storedProcedure.SchemaName}.{storedProcedure.Name} initial load (ticks={currentModifiedTicks})");
                }

                string definition = null;
                if (!canSkipDetails)
                {
                    var def = await dbContext.StoredProcedureDefinitionAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    definition = def?.Definition;
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
                    }
                }
                storedProcedure.ModifiedTicks = currentModifiedTicks;

                // Removed legacy AsJson suffix heuristic: JSON detection now relies solely on content analysis (FOR JSON ...)

                if (!canSkipDetails)
                {
                    var inputs = await dbContext.StoredProcedureInputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    storedProcedure.Input = inputs?.Select(i => new StoredProcedureInputModel(i)).ToList();

                    var output = await dbContext.StoredProcedureOutputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    var outputModels = output?.Select(i => new StoredProcedureOutputModel(i)).ToList() ?? new List<StoredProcedureOutputModel>();

                    // Unified rule: Never persist legacy Output anymore; rely solely on synthesized ResultSets
                    var anyJson = storedProcedure.Content?.ResultSets?.Any(r => r.ReturnsJson) == true;

                    // Synthesize ResultSets for non-JSON procedures so that every procedure has at least one ResultSet entry.
                    if (!anyJson)
                    {
                        var existingSets = storedProcedure.Content?.ResultSets ?? Array.Empty<StoredProcedureContentModel.ResultSet>();
                        if (!existingSets.Any())
                        {
                            // Map classic output columns to a synthetic ResultSet (ReturnsJson = false)
                            var syntheticColumns = outputModels
                                .Select(o => new StoredProcedureContentModel.ResultColumn
                                {
                                    Name = o.Name,
                                    JsonPath = null,
                                    SourceSchema = null,
                                    SourceTable = null,
                                    SourceColumn = null,
                                    SqlTypeName = o.SqlTypeName,
                                    IsNullable = o.IsNullable,
                                    MaxLength = o.MaxLength
                                }).ToArray();
                            var syntheticSet = new StoredProcedureContentModel.ResultSet
                            {
                                ReturnsJson = false,
                                ReturnsJsonArray = false,
                                ReturnsJsonWithoutArrayWrapper = false,
                                JsonRootProperty = null,
                                Columns = syntheticColumns
                            };
                            // Legacy FOR JSON (single synthetic column) upgrade: detect nvarchar(max) JSON_F52E... column and mark as JSON
                            if (syntheticSet.Columns.Count == 1 &&
                                string.Equals(syntheticSet.Columns[0].Name, "JSON_F52E2B61-18A1-11d1-B105-00805F49916B", StringComparison.OrdinalIgnoreCase) &&
                                (syntheticSet.Columns[0].SqlTypeName?.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase) ?? false))
                            {
                                syntheticSet = new StoredProcedureContentModel.ResultSet
                                {
                                    ReturnsJson = true,
                                    ReturnsJsonArray = true,
                                    ReturnsJsonWithoutArrayWrapper = false,
                                    JsonRootProperty = null,
                                    Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>()
                                };
                                if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                                    consoleService.Verbose($"[proc-json-legacy-upgrade] {storedProcedure.SchemaName}.{storedProcedure.Name} single synthetic FOR JSON column upgraded to JSON.");
                            }
                            // Reconstruct content object preserving existing parse flags
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
                                FirstParseError = storedProcedure.Content?.FirstParseError
                            };
                            if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                                consoleService.Verbose($"[proc-resultset-synth] {storedProcedure.SchemaName}.{storedProcedure.Name} classic output mapped to ResultSets");
                        }
                    }
                }
                else if (canSkipDetails && (storedProcedure.Input == null))
                {
                    // Procedure body unchanged but we never persisted inputs/outputs previously – hydrate minimally for persistence.
                    var inputs = await dbContext.StoredProcedureInputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    storedProcedure.Input = inputs?.Select(i => new StoredProcedureInputModel(i)).ToList();

                    var output = await dbContext.StoredProcedureOutputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    var skipOutputModels = output?.Select(i => new StoredProcedureOutputModel(i)).ToList() ?? new List<StoredProcedureOutputModel>();
                    var anyJson = storedProcedure.Content?.ResultSets?.Any(r => r.ReturnsJson) == true;
                    if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                        consoleService.Verbose($"[proc-skip-hydrate] {storedProcedure.SchemaName}.{storedProcedure.Name} inputs/outputs loaded (cache metadata backfill)");

                    // Also synthesize ResultSets for non-JSON procedures on skip path if not yet present
                    if (!anyJson)
                    {
                        var existingSets = storedProcedure.Content?.ResultSets ?? Array.Empty<StoredProcedureContentModel.ResultSet>();
                        if (!existingSets.Any() && skipOutputModels.Any())
                        {
                            var syntheticColumns = skipOutputModels.Select(o => new StoredProcedureContentModel.ResultColumn
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
                                ReturnsJsonWithoutArrayWrapper = false,
                                JsonRootProperty = null,
                                Columns = syntheticColumns
                            };
                            storedProcedure.Content = new StoredProcedureContentModel
                            {
                                Definition = storedProcedure.Content?.Definition,
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
                                FirstParseError = storedProcedure.Content?.FirstParseError
                            };
                            if (jsonTypeLogLevel == JsonTypeLogLevel.Detailed)
                                consoleService.Verbose($"[proc-resultset-synth] {storedProcedure.SchemaName}.{storedProcedure.Name} classic output mapped to ResultSets (skip path)");
                        }
                    }

                    // Removed legacy AsJson skip-path heuristic and adjustments

                    // If heuristic JSON applied (or existing) and output is only the generic FOR JSON root column -> remove it (metadata expresses JSON via ResultSets)
                    // Cleanup logic no longer needed since JSON outputs are fully suppressed.
                }

                // record cache entry
                updatedSnapshot.Procedures.Add(new ProcedureCacheEntry
                {
                    Schema = storedProcedure.SchemaName,
                    Name = storedProcedure.Name,
                    ModifiedTicks = currentModifiedTicks
                });
            }

            var tableTypeModels = new List<TableTypeModel>();
            foreach (var tableType in tableTypes.Where(i => i.SchemaName.Equals(schema.Name)))
            {
                var columns = await dbContext.TableTypeColumnListAsync(tableType.UserTypeId ?? -1, cancellationToken);
                var tableTypeModel = new TableTypeModel(tableType, columns);
                tableTypeModels.Add(tableTypeModel);
            }

            schema.TableTypes = tableTypeModels;
        }

        // Forwarding normalization: clone ResultSets from executed procedure for wrapper procs
        try
        {
            var allProcedures = schemas.SelectMany(s => s.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>()).ToList();
            var procLookup = allProcedures
                .ToDictionary(p => ($"{p.SchemaName}.{p.Name}"), p => p, StringComparer.OrdinalIgnoreCase);

            foreach (var proc in allProcedures)
            {
                var content = proc.Content;
                if (content == null) continue;
                var hasSets = content.ResultSets != null && content.ResultSets.Any();
                bool onlyEmptyJsonSets = hasSets && content.ResultSets.All(rs => rs.ReturnsJson && (rs.Columns == null || rs.Columns.Count == 0));
                if (hasSets && !onlyEmptyJsonSets)
                {
                    consoleService.Verbose($"[proc-forward-skip] {proc.SchemaName}.{proc.Name} has concrete result sets (hasSets && !onlyEmptyJsonSets)");
                    continue; // has meaningful sets -> skip
                }
                if (content.ExecutedProcedures == null || content.ExecutedProcedures.Count != 1)
                {
                    if (content.ExecutedProcedures == null || content.ExecutedProcedures.Count == 0)
                        consoleService.Verbose($"[proc-forward-skip] {proc.SchemaName}.{proc.Name} no executed procedures captured");
                    else
                        consoleService.Verbose($"[proc-forward-skip] {proc.SchemaName}.{proc.Name} multiple executed procedures ({content.ExecutedProcedures.Count})");
                    continue; // only single EXEC wrapper
                }
                if (content.ContainsSelect || content.ContainsInsert || content.ContainsUpdate || content.ContainsDelete || content.ContainsMerge)
                {
                    // If it has its own SELECT but only produced empty JSON sets, allow upgrade; otherwise skip
                    if (!onlyEmptyJsonSets)
                    {
                        consoleService.Verbose($"[proc-forward-skip] {proc.SchemaName}.{proc.Name} contains its own DML/SELECT and sets not only empty json");
                        continue;
                    }
                }
                var target = content.ExecutedProcedures[0];
                if (target == null) continue;
                if (!procLookup.TryGetValue($"{target.Schema}.{target.Name}", out var targetProc)) continue;
                var targetSets = targetProc.Content?.ResultSets;
                if (targetSets == null || !targetSets.Any()) continue;

                // Clone target sets
                var clonedSets = targetSets.Select(rs => new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = rs.ReturnsJson,
                    ReturnsJsonArray = rs.ReturnsJsonArray,
                    ReturnsJsonWithoutArrayWrapper = rs.ReturnsJsonWithoutArrayWrapper,
                    JsonRootProperty = rs.JsonRootProperty,
                    ExecSourceSchemaName = target.Schema,
                    ExecSourceProcedureName = target.Name,
                    Columns = rs.Columns.Select(c => new StoredProcedureContentModel.ResultColumn
                    {
                        Name = c.Name,
                        JsonPath = c.JsonPath,
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
                        JsonResult = c.JsonResult == null ? null : new StoredProcedureContentModel.JsonResultModel
                        {
                            ReturnsJson = c.JsonResult.ReturnsJson,
                            ReturnsJsonArray = c.JsonResult.ReturnsJsonArray,
                            ReturnsJsonWithoutArrayWrapper = c.JsonResult.ReturnsJsonWithoutArrayWrapper,
                            JsonRootProperty = c.JsonResult.JsonRootProperty,
                            Columns = c.JsonResult.Columns.ToArray()
                        }
                    }).ToArray()
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
                    ResultSets = clonedSets,
                    UsedFallbackParser = proc.Content.UsedFallbackParser,
                    ParseErrorCount = proc.Content.ParseErrorCount,
                    FirstParseError = proc.Content.FirstParseError,
                    ExecutedProcedures = proc.Content.ExecutedProcedures
                };
                consoleService.Verbose($"[proc-forward{(onlyEmptyJsonSets ? "-upgrade" : string.Empty)}] {proc.SchemaName}.{proc.Name} forwarded {clonedSets.Length} result set(s) from {target.Schema}.{target.Name}");
            }
        }
        catch { /* best effort */ }

        if (totalSpCount > 0)
        {
            consoleService.DrawProgressBar(100);
            consoleService.CompleteProgress(true, $"Loaded {totalSpCount} stored procedures");
        }

        // Persist updated cache (best-effort)
        var saveStart = DateTime.UtcNow;
        if (!disableCache)
        {
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

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

        // overwrite with current config
        if (config?.Schema != null)
        {
            foreach (var schema in schemas)
            {
                // ! Do not compare with Id. The Id is different for each SQL-Server Instance
                var currentSchema = config.Schema.SingleOrDefault(i => i.Name == schema.Name);
                // TODO define a global and local Property "onNewSchemaFound" (IGNORE, BUILD, WARN, PROMPT) to set the default Status
                schema.Status = (currentSchema != null)
                    ? currentSchema.Status
                    : config.Project.DefaultSchemaStatus;
            }
        }

        // reorder schemas, ignored at top
        schemas = schemas.OrderByDescending(schema => schema.Status).ToList();

        var schemaListString = string.Join(',', schemas.Where(i => i.Status != SchemaStatusEnum.Ignore).Select(i => $"'{i.Name}'"));
        if (string.IsNullOrEmpty(schemaListString))
        {
            consoleService.Warn("No schemas found or all schemas ignored!");
            return schemas;
        }

        var storedProcedures = await dbContext.StoredProcedureListAsync(schemaListString, cancellationToken);

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
                var previousModifiedTicks = cache?.GetModifiedTicks(storedProcedure.SchemaName, storedProcedure.Name);
                var canSkipDetails = !disableCache && previousModifiedTicks.HasValue && previousModifiedTicks.Value == currentModifiedTicks;
                if (canSkipDetails)
                {
                    consoleService.Verbose($"[proc-skip] {storedProcedure.SchemaName}.{storedProcedure.Name} unchanged (ticks={currentModifiedTicks})");
                }
                else if (previousModifiedTicks.HasValue)
                {
                    consoleService.Verbose($"[proc-loaded] {storedProcedure.SchemaName}.{storedProcedure.Name} updated {previousModifiedTicks.Value} -> {currentModifiedTicks}");
                }
                else
                {
                    consoleService.Verbose($"[proc-loaded] {storedProcedure.SchemaName}.{storedProcedure.Name} initial load (ticks={currentModifiedTicks})");
                }

                string definition = null;
                if (!canSkipDetails)
                {
                    var def = await dbContext.StoredProcedureDefinitionAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    definition = def?.Definition;
                    storedProcedure.Content = StoredProcedureContentModel.Parse(definition, storedProcedure.SchemaName);
                    if (storedProcedure.Content?.UsedFallbackParser == true)
                    {
                        consoleService.Verbose($"[proc-parse-fallback] {storedProcedure.SchemaName}.{storedProcedure.Name} parse errors={storedProcedure.Content.ParseErrorCount} first='{storedProcedure.Content.FirstParseError}'");
                    }
                    else if (storedProcedure.Content?.JsonResultSets?.Count > 1)
                    {
                        consoleService.Verbose($"[proc-json-multi] {storedProcedure.SchemaName}.{storedProcedure.Name} sets={storedProcedure.Content.JsonResultSets.Count}");
                    }
                }
                storedProcedure.ModifiedTicks = currentModifiedTicks;

                // Heuristic: If parser did not detect JSON but name ends with AsJson treat it as JSON returning (string payload)
                var existingPrimaryJson = storedProcedure.Content?.JsonResultSets?.FirstOrDefault();
                var hasJson = existingPrimaryJson?.ReturnsJson == true;
                if (!canSkipDetails && !hasJson && storedProcedure.Name.EndsWith("AsJson", StringComparison.OrdinalIgnoreCase))
                {
                    // Synthetische JSON ResultSet hinzufügen
                    var defForHeuristic = storedProcedure.Content?.Definition ?? definition ?? string.Empty;
                    var withoutArray = defForHeuristic.IndexOf("WITHOUT ARRAY WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0
                        || defForHeuristic.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;
                    string rootProp = null;
                    // naive ROOT('name') extraction
                    const string rootToken = "ROOT(";
                    var rootIx = defForHeuristic.IndexOf(rootToken, StringComparison.OrdinalIgnoreCase);
                    if (rootIx >= 0)
                    {
                        var startQuote = defForHeuristic.IndexOf('\'', rootIx);
                        var endQuote = startQuote >= 0 ? defForHeuristic.IndexOf('\'', startQuote + 1) : -1;
                        if (startQuote >= 0 && endQuote > startQuote)
                        {
                            rootProp = defForHeuristic.Substring(startQuote + 1, endQuote - startQuote - 1);
                        }
                    }
                    var newSet = new StoredProcedureContentModel.JsonResultSet
                    {
                        ReturnsJson = true,
                        ReturnsJsonArray = !withoutArray,
                        ReturnsJsonWithoutArrayWrapper = withoutArray,
                        JsonRootProperty = rootProp,
                        JsonColumns = Array.Empty<StoredProcedureContentModel.JsonColumn>()
                    };
                    var existingSets = storedProcedure.Content?.JsonResultSets ?? Array.Empty<StoredProcedureContentModel.JsonResultSet>();
                    storedProcedure.Content = new StoredProcedureContentModel
                    {
                        Definition = storedProcedure.Content?.Definition ?? definition,
                        // only keep flags still present
                        ContainsSelect = storedProcedure.Content?.ContainsSelect ?? false,
                        ContainsInsert = storedProcedure.Content?.ContainsInsert ?? false,
                        ContainsUpdate = storedProcedure.Content?.ContainsUpdate ?? false,
                        ContainsDelete = storedProcedure.Content?.ContainsDelete ?? false,
                        ContainsMerge = storedProcedure.Content?.ContainsMerge ?? false,
                        ContainsOpenJson = storedProcedure.Content?.ContainsOpenJson ?? false,
                        JsonResultSets = existingSets.Any() ? existingSets : new[] { newSet },
                        UsedFallbackParser = storedProcedure.Content?.UsedFallbackParser ?? false,
                        ParseErrorCount = storedProcedure.Content?.ParseErrorCount,
                        FirstParseError = storedProcedure.Content?.FirstParseError
                    };
                    consoleService.Verbose($"[proc-json-heuristic] {storedProcedure.SchemaName}.{storedProcedure.Name} name heuristic applied (no FOR JSON detected)");
                }
                else if (!canSkipDetails && storedProcedure.Name.EndsWith("AsJson", StringComparison.OrdinalIgnoreCase) && !hasJson)
                {
                    consoleService.Verbose($"[warn] {storedProcedure.SchemaName}.{storedProcedure.Name} ends with AsJson but no JSON detected");
                }

                if (!canSkipDetails)
                {
                    var inputs = await dbContext.StoredProcedureInputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    storedProcedure.Input = inputs?.Select(i => new StoredProcedureInputModel(i)).ToList();

                    var output = await dbContext.StoredProcedureOutputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    var outputModels = output?.Select(i => new StoredProcedureOutputModel(i)).ToList() ?? new List<StoredProcedureOutputModel>();

                    var primaryJson = storedProcedure.Content?.JsonResultSets?.FirstOrDefault();
                    var jsonColumns = primaryJson?.JsonColumns;
                    if (primaryJson?.ReturnsJson == true && jsonColumns?.Any() == true)
                    {
                        var jsonOutputs = new List<StoredProcedureOutputModel>();
                        foreach (var jsonColumn in jsonColumns)
                        {
                            Column columnInfo = null;
                            if (!string.IsNullOrEmpty(jsonColumn.SourceTable) && !string.IsNullOrEmpty(jsonColumn.SourceColumn))
                            {
                                columnInfo = await dbContext.TableColumnAsync(jsonColumn.SourceSchema ?? storedProcedure.SchemaName, jsonColumn.SourceTable, jsonColumn.SourceColumn, cancellationToken);
                            }

                            var outputName = jsonColumn.Name ?? jsonColumn.SourceColumn ?? "Value";

                            var column = new StoredProcedureOutput
                            {
                                Name = outputName,
                                IsNullable = columnInfo?.IsNullable ?? true,
                                SqlTypeName = columnInfo?.SqlTypeName ?? "nvarchar(max)",
                                MaxLength = columnInfo?.MaxLength ?? 0
                            };

                            jsonOutputs.Add(new StoredProcedureOutputModel(column));
                        }

                        storedProcedure.Output = jsonOutputs.Any() ? jsonOutputs : outputModels;
                    }
                    else
                    {
                        storedProcedure.Output = outputModels;
                    }
                }
                else if (canSkipDetails && (storedProcedure.Input == null || storedProcedure.Output == null))
                {
                    // Procedure body unchanged but we never persisted inputs/outputs previously – hydrate minimally for persistence.
                    var inputs = await dbContext.StoredProcedureInputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    storedProcedure.Input = inputs?.Select(i => new StoredProcedureInputModel(i)).ToList();

                    var output = await dbContext.StoredProcedureOutputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                    storedProcedure.Output = output?.Select(i => new StoredProcedureOutputModel(i)).ToList() ?? new List<StoredProcedureOutputModel>();
                    consoleService.Verbose($"[proc-skip-hydrate] {storedProcedure.SchemaName}.{storedProcedure.Name} inputs/outputs loaded (cache metadata backfill)");

                    // Apply the same AsJson heuristic also on skip path if no JSON sets exist yet
                    if (storedProcedure.Name.EndsWith("AsJson", StringComparison.OrdinalIgnoreCase) && (storedProcedure.Content?.JsonResultSets == null || !storedProcedure.Content.JsonResultSets.Any()))
                    {
                        storedProcedure.Content ??= new StoredProcedureContentModel();
                        var defForHeuristic = storedProcedure.Content?.Definition ?? string.Empty;
                        var withoutArray = defForHeuristic.IndexOf("WITHOUT ARRAY WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0
                            || defForHeuristic.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;
                        string rootProp = null;
                        const string rootToken = "ROOT(";
                        var rootIx = defForHeuristic.IndexOf(rootToken, StringComparison.OrdinalIgnoreCase);
                        if (rootIx >= 0)
                        {
                            var startQuote = defForHeuristic.IndexOf('\'', rootIx);
                            var endQuote = startQuote >= 0 ? defForHeuristic.IndexOf('\'', startQuote + 1) : -1;
                            if (startQuote >= 0 && endQuote > startQuote)
                            {
                                rootProp = defForHeuristic.Substring(startQuote + 1, endQuote - startQuote - 1);
                            }
                        }
                        storedProcedure.Content = new StoredProcedureContentModel
                        {
                            Definition = storedProcedure.Content.Definition,
                            ContainsSelect = storedProcedure.Content.ContainsSelect,
                            ContainsInsert = storedProcedure.Content.ContainsInsert,
                            ContainsUpdate = storedProcedure.Content.ContainsUpdate,
                            ContainsDelete = storedProcedure.Content.ContainsDelete,
                            ContainsMerge = storedProcedure.Content.ContainsMerge,
                            ContainsOpenJson = storedProcedure.Content.ContainsOpenJson,
                            JsonResultSets = new[]
                            {
                                new StoredProcedureContentModel.JsonResultSet
                                {
                                    ReturnsJson = true,
                                    ReturnsJsonArray = !withoutArray,
                                    ReturnsJsonWithoutArrayWrapper = withoutArray,
                                    JsonRootProperty = rootProp,
                                    JsonColumns = Array.Empty<StoredProcedureContentModel.JsonColumn>()
                                }
                            },
                            UsedFallbackParser = storedProcedure.Content.UsedFallbackParser,
                            ParseErrorCount = storedProcedure.Content.ParseErrorCount,
                            FirstParseError = storedProcedure.Content.FirstParseError
                        };
                        consoleService.Verbose($"[proc-json-heuristic] {storedProcedure.SchemaName}.{storedProcedure.Name} heuristic applied on skip path");
                    }
                    else if (storedProcedure.Name.EndsWith("AsJson", StringComparison.OrdinalIgnoreCase) && storedProcedure.Content?.JsonResultSets?.Any() == true)
                    {
                        // Adjust existing heuristic set if definition indicates WITHOUT_ARRAY_WRAPPER but flags differ
                        var set = storedProcedure.Content.JsonResultSets.First();
                        var def = storedProcedure.Content.Definition;
                        if (string.IsNullOrEmpty(def))
                        {
                            // fetch definition lazily on skip path to refine JSON flags
                            var defResult = await dbContext.StoredProcedureDefinitionAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                            def = defResult?.Definition ?? string.Empty;
                        }
                        var withoutArrayNow = def.IndexOf("WITHOUT ARRAY WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0 || def.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (withoutArrayNow && !set.ReturnsJsonWithoutArrayWrapper)
                        {
                            storedProcedure.Content = new StoredProcedureContentModel
                            {
                                Definition = storedProcedure.Content.Definition,
                                ContainsSelect = storedProcedure.Content.ContainsSelect,
                                ContainsInsert = storedProcedure.Content.ContainsInsert,
                                ContainsUpdate = storedProcedure.Content.ContainsUpdate,
                                ContainsDelete = storedProcedure.Content.ContainsDelete,
                                ContainsMerge = storedProcedure.Content.ContainsMerge,
                                ContainsOpenJson = storedProcedure.Content.ContainsOpenJson,
                                JsonResultSets = new[] {
                                    new StoredProcedureContentModel.JsonResultSet
                                    {
                                        ReturnsJson = true,
                                        ReturnsJsonArray = false,
                                        ReturnsJsonWithoutArrayWrapper = true,
                                        JsonRootProperty = set.JsonRootProperty,
                                        JsonColumns = set.JsonColumns
                                    }
                                },
                                UsedFallbackParser = storedProcedure.Content.UsedFallbackParser,
                                ParseErrorCount = storedProcedure.Content.ParseErrorCount,
                                FirstParseError = storedProcedure.Content.FirstParseError
                            };
                            consoleService.Verbose($"[proc-json-adjust] {storedProcedure.SchemaName}.{storedProcedure.Name} set WITHOUT_ARRAY_WRAPPER after skip");
                        }
                    }

                    // If heuristic JSON applied (or existing) and output is only the generic FOR JSON root column -> remove it (metadata expresses JSON via JsonResultSets)
                    if (storedProcedure.Content?.JsonResultSets?.Any(r => r.ReturnsJson) == true && storedProcedure.Output != null && storedProcedure.Output.Count() == 1)
                    {
                        var firstOut = storedProcedure.Output.First();
                        if (firstOut.Name.StartsWith("JSON_", StringComparison.OrdinalIgnoreCase))
                        {
                            storedProcedure.Output = null; // drop placeholder
                            consoleService.Verbose($"[proc-json-cleanup] {storedProcedure.SchemaName}.{storedProcedure.Name} removed generic JSON output column");
                        }
                    }
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

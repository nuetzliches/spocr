using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpocR.AutoUpdater;
using SpocR.CodeGenerators;
using SpocR.Commands;
using SpocR.Commands.Spocr;
using SpocR.DataContext;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Models;
using SpocR.Services;
using SpocR.DataContext.Queries;
using SpocR.DataContext.Models;

namespace SpocR.Managers;

public class SpocrManager(
    SpocrService service,
    OutputService output,
    CodeGenerationOrchestrator orchestrator,
    SpocrProjectManager projectManager,
    IConsoleService consoleService,
    SchemaManager schemaManager,
    FileManager<GlobalConfigurationModel> globalConfigFile,
    FileManager<ConfigurationModel> configFile,
    DbContext dbContext,
    AutoUpdaterService autoUpdaterService,
    ISchemaSnapshotService schemaSnapshotService
)
{
    public async Task<ExecuteResultEnum> CreateAsync(ICreateCommandOptions options)
    {
        await RunAutoUpdateAsync(options);

        if (configFile.Exists())
        {
            consoleService.Error("Configuration already exists");
            consoleService.Output($"\tTo view current configuration, run '{Constants.Name} status'");
            return ExecuteResultEnum.Error;
        }

        if (!options.Quiet && !options.Force)
        {
            var proceed = consoleService.GetYesNo($"Create a new {Constants.ConfigurationFile} file?", true);
            if (!proceed) return ExecuteResultEnum.Aborted;
        }

        var targetFramework = options.TargetFramework;
        if (!options.Quiet)
        {
            targetFramework = consoleService.GetString("TargetFramework:", targetFramework);
        }

        var appNamespace = options.Namespace;
        if (!options.Quiet)
        {
            appNamespace = consoleService.GetString("Your Namespace:", appNamespace);
        }

        var connectionString = "";
        // Role deprecated – Optionen nur noch für Migrationshinweis anzeigen falls Benutzer explizit etwas eingibt
        var roleKindString = options.Role;
        RoleKindEnum roleKind = RoleKindEnum.Default; // Immer Default setzen
        string libNamespace = null;
        if (!options.Quiet && !string.IsNullOrWhiteSpace(roleKindString))
        {
            if (Enum.TryParse(roleKindString, true, out RoleKindEnum parsed) && parsed != RoleKindEnum.Default)
            {
                consoleService.Warn("[deprecation] Providing a role is deprecated and ignored. Default role is always applied.");
            }
        }

        var config = service.GetDefaultConfiguration(targetFramework, appNamespace, connectionString, roleKind, libNamespace);

        if (options.DryRun)
        {
            consoleService.PrintConfiguration(config);
            consoleService.PrintDryRunMessage();
        }
        else
        {
            await configFile.SaveAsync(config);
            projectManager.Create(options);

            if (!options.Quiet)
            {
                consoleService.Output($"{Constants.ConfigurationFile} successfully created.");
            }
        }

        return ExecuteResultEnum.Succeeded;
    }

    public async Task<ExecuteResultEnum> PullAsync(ICommandOptions options)
    {
        await RunAutoUpdateAsync(options);

        if (!configFile.Exists())
        {
            consoleService.Error("Configuration file not found");
            consoleService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
            return ExecuteResultEnum.Error;
        }

        var config = await LoadAndMergeConfigurationsAsync();

        // Migration: move ignored schemas from legacy config.Schema to Project.IgnoredSchemas
        try
        {
            if (config?.Project != null && config.Schema != null)
            {
                if ((config.Project.IgnoredSchemas == null || config.Project.IgnoredSchemas.Count == 0) && config.Schema.Count > 0)
                {
                    var ignored = config.Schema.Where(s => s.Status == SchemaStatusEnum.Ignore)
                        .Select(s => s.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (ignored.Count > 0)
                    {
                        config.Project.IgnoredSchemas = ignored;
                        consoleService.Info($"[migration] Collected {ignored.Count} ignored schema name(s) into Project.IgnoredSchemas");
                    }
                }
                // Always drop legacy schema node after migration pass
                config.Schema = null;
                await configFile.SaveAsync(config);
            }
        }
        catch (Exception mx)
        {
            consoleService.Verbose($"[migration-warn] {mx.Message}");
        }

        if (await RunConfigVersionCheckAsync(options) == ExecuteResultEnum.Aborted)
            return ExecuteResultEnum.Aborted;

        if (string.IsNullOrWhiteSpace(config?.Project?.DataBase?.ConnectionString))
        {
            consoleService.Error("Missing database connection string");
            consoleService.Output($"\tTo configure database access, run '{Constants.Name} set --cs \"your-connection-string\"'");
            return ExecuteResultEnum.Error;
        }

        dbContext.SetConnectionString(config.Project.DataBase.ConnectionString);
        consoleService.PrintTitle("Pulling database schema from database");

        // After migration the legacy schema list is no longer persisted; we still enumerate live schemas below.
        var previousSchemas = new Dictionary<string, SchemaModel>(StringComparer.OrdinalIgnoreCase);

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        List<SchemaModel> schemas = null;
        var originalIgnored = config?.Project?.IgnoredSchemas == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(config.Project.IgnoredSchemas, StringComparer.OrdinalIgnoreCase);
        try
        {
            schemas = await schemaManager.ListAsync(config, options.NoCache);

            if (schemas == null || schemas.Count == 0)
            {
                consoleService.Error("No database schemas retrieved");
                consoleService.Output("\tPlease check your database connection and permissions");
                return ExecuteResultEnum.Error;
            }

            // SchemaManager now provides final statuses (including auto-ignore of new schemas where applicable).

            // --- Snapshot Construction (experimental) ---
            try
            {
                // Collect build schema names (status Build)
                var buildSchemas = schemas.Where(s => s.Status == SchemaStatusEnum.Build).Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // Load UDTTs for ALL schemas (independent of build status)
                var allSchemaNames = schemas.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var schemaListLiteral = string.Join(',', allSchemaNames.Select(n => $"'{n}'"));
                var allTableTypes = await dbContext.TableTypeListAsync(schemaListLiteral, cancellationToken: default);
                var udttModels = new List<(string Schema, string Name, int? Id, List<Column> Columns)>();
                foreach (var tt in allTableTypes)
                {
                    var cols = await dbContext.TableTypeColumnListAsync(tt.UserTypeId ?? -1, cancellationToken: default);
                    udttModels.Add((tt.SchemaName, tt.Name, tt.UserTypeId, cols));
                }

                // Build UDTT lookup by (schema,name)
                var udttLookup = udttModels.ToDictionary(k => ($"{k.Schema}.{k.Name}").ToLowerInvariant(), v => v, StringComparer.OrdinalIgnoreCase);

                // Enrich JSON result set columns ONLY with SqlTypeName derived from referenced UDTTs (first stage)
                foreach (var schema in schemas)
                {
                    if (schema?.StoredProcedures == null) continue;
                    foreach (var sp in schema.StoredProcedures)
                    {
                        var sets = sp.Content?.ResultSets;
                        if (sets == null || sets.Count == 0) continue;

                        // Collect candidate UDTT columns from table-type inputs
                        var tableTypeInputs = (sp.Input ?? []).Where(i => i.IsTableType == true && !string.IsNullOrEmpty(i.TableTypeSchemaName) && !string.IsNullOrEmpty(i.TableTypeName)).ToList();
                        var columnMap = new Dictionary<string, (string SqlType, int MaxLen, bool IsNullable)>(StringComparer.OrdinalIgnoreCase);
                        foreach (var input in tableTypeInputs)
                        {
                            var key = ($"{input.TableTypeSchemaName}.{input.TableTypeName}").ToLowerInvariant();
                            if (!udttLookup.TryGetValue(key, out var ttMeta)) continue;
                            foreach (var c in ttMeta.Columns)
                            {
                                // Only add if not already present to keep first occurrence (avoid ambiguity)
                                if (!columnMap.ContainsKey(c.Name))
                                {
                                    columnMap[c.Name] = (c.SqlTypeName, c.MaxLength, c.IsNullable);
                                }
                            }
                        }

                        var modified = false;
                        var newSets = new List<StoredProcedureContentModel.ResultSet>();
                        // Stage1: no per-procedure summary (handled in stage2); keep parsing lean
                        foreach (var set in sets)
                        {
                            if (!set.ReturnsJson || set.Columns == null || set.Columns.Count == 0)
                            {
                                newSets.Add(set);
                                continue;
                            }
                            var newCols = new List<StoredProcedureContentModel.ResultColumn>();
                            foreach (var col in set.Columns)
                            {
                                if (!string.IsNullOrWhiteSpace(col.SqlTypeName))
                                {
                                    newCols.Add(col);
                                    continue;
                                }
                                string sqlType = null; int maxLen = 0; bool isNull = false;
                                if (columnMap.TryGetValue(col.Name, out var meta))
                                {
                                    sqlType = meta.SqlType;
                                    maxLen = meta.MaxLen;
                                    isNull = meta.IsNullable;
                                    if (options.Verbose) consoleService.Verbose($"[json-type-udtt] {sp.SchemaName}.{sp.Name} {col.Name} -> {sqlType}");
                                }
                                if (sqlType == null)
                                {
                                    newCols.Add(col); // unchanged
                                    continue;
                                }
                                modified = true;
                                newCols.Add(new StoredProcedureContentModel.ResultColumn
                                {
                                    JsonPath = col.JsonPath,
                                    Name = col.Name,
                                    SourceSchema = col.SourceSchema,
                                    SourceTable = col.SourceTable,
                                    SourceColumn = col.SourceColumn,
                                    SqlTypeName = sqlType,
                                    IsNullable = isNull,
                                    MaxLength = maxLen
                                });
                            }
                            if (modified)
                            {
                                newSets.Add(new StoredProcedureContentModel.ResultSet
                                {
                                    ReturnsJson = set.ReturnsJson,
                                    ReturnsJsonArray = set.ReturnsJsonArray,
                                    ReturnsJsonWithoutArrayWrapper = set.ReturnsJsonWithoutArrayWrapper,
                                    JsonRootProperty = set.JsonRootProperty,
                                    Columns = newCols.ToArray()
                                });
                            }
                            else
                            {
                                newSets.Add(set);
                            }
                        }
                        if (modified)
                        {
                            // replace content with enriched sets
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
                                ResultSets = newSets.ToArray(),
                                UsedFallbackParser = sp.Content.UsedFallbackParser,
                                ParseErrorCount = sp.Content.ParseErrorCount,
                                FirstParseError = sp.Content.FirstParseError
                            };
                        }
                    }
                }

                var jsonTypeLogLevel = config.Project?.JsonTypeLogLevel ?? JsonTypeLogLevel.Detailed;
                var enrichmentStats = new JsonTypeEnrichmentStats();
                var enricher = new JsonResultTypeEnricher(dbContext, consoleService);
                foreach (var schema in schemas)
                {
                    if (schema?.StoredProcedures == null) continue;
                    foreach (var sp in schema.StoredProcedures)
                    {
                        await enricher.EnrichAsync(sp, options.Verbose, jsonTypeLogLevel, enrichmentStats, System.Threading.CancellationToken.None);
                    }
                }

                // Build snapshot
                var procedures = schemas.SelectMany(sc => sc.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>())
                    .Select(sp =>
                    {
                        var rsRaw = (sp.Content?.ResultSets ?? Array.Empty<StoredProcedureContentModel.ResultSet>());
                        // Remove placeholder empty entries (no columns, no JSON flags) to avoid fake ResultSets (e.g. BannerDelete)
                        var rsFiltered = rsRaw.Where(r => r.ReturnsJson || r.ReturnsJsonArray || r.ReturnsJsonWithoutArrayWrapper || (r.Columns?.Count > 0)).ToArray();
                        return new SnapshotProcedure
                        {
                            Schema = sp.SchemaName,
                            Name = sp.Name,
                            Inputs = (sp.Input ?? []).Select(i => new SnapshotInput
                            {
                                Name = i.Name,
                                IsTableType = i.IsTableType == true,
                                TableTypeSchema = i.TableTypeSchemaName,
                                TableTypeName = i.TableTypeName,
                                IsOutput = i.IsOutput,
                                SqlTypeName = i.SqlTypeName,
                                IsNullable = i.IsNullable ?? false,
                                MaxLength = i.MaxLength ?? 0
                            }).ToList(),
                            ResultSets = rsFiltered.Select(rs => new SnapshotResultSet
                            {
                                ReturnsJson = rs.ReturnsJson,
                                ReturnsJsonArray = rs.ReturnsJsonArray,
                                ReturnsJsonWithoutArrayWrapper = rs.ReturnsJsonWithoutArrayWrapper,
                                JsonRootProperty = rs.JsonRootProperty,
                                ExecSourceSchemaName = rs.ExecSourceSchemaName,
                                ExecSourceProcedureName = rs.ExecSourceProcedureName,
                                HasSelectStar = rs.HasSelectStar,
                                Columns = rs.Columns.Select(c => new SnapshotResultColumn
                                {
                                    Name = c.Name,
                                    // Fallback: Ensure JSON result columns always have a SqlTypeName so generators can rely on presence.
                                    // If parser/enrichment couldn't resolve a concrete type, we default to nvarchar(max).
                                    SqlTypeName = string.IsNullOrWhiteSpace(c.SqlTypeName) && rs.ReturnsJson ? "nvarchar(max)" : c.SqlTypeName,
                                    IsNullable = c.IsNullable ?? false,
                                    MaxLength = c.MaxLength ?? 0,
                                    UserTypeSchemaName = c.UserTypeSchemaName,
                                    UserTypeName = c.UserTypeName,
                                    JsonPath = c.JsonPath,
                                    JsonResult = c.JsonResult == null ? null : new SnapshotNestedJson
                                    {
                                        ReturnsJson = c.JsonResult.ReturnsJson,
                                        ReturnsJsonArray = c.JsonResult.ReturnsJsonArray,
                                        ReturnsJsonWithoutArrayWrapper = c.JsonResult.ReturnsJsonWithoutArrayWrapper,
                                        JsonRootProperty = c.JsonResult.JsonRootProperty,
                                        Columns = c.JsonResult.Columns?.Select(n => new SnapshotResultColumn
                                        {
                                            Name = n.Name,
                                            SqlTypeName = string.IsNullOrWhiteSpace(n.SqlTypeName) && c.JsonResult.ReturnsJson ? "nvarchar(max)" : n.SqlTypeName,
                                            IsNullable = n.IsNullable ?? false,
                                            MaxLength = n.MaxLength ?? 0,
                                            UserTypeSchemaName = n.UserTypeSchemaName,
                                            UserTypeName = n.UserTypeName,
                                            JsonPath = n.JsonPath
                                        }).ToList() ?? new List<SnapshotResultColumn>()
                                    }
                                }).ToList()
                            }).ToList()
                        };
                    }).ToList();

                // UDTT hashing helper
                string HashUdtt(string schema, string name, IEnumerable<Column> cols)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append(schema).Append('|').Append(name).Append('|');
                    foreach (var c in cols)
                    {
                        sb.Append(c.Name).Append(':').Append(c.SqlTypeName).Append(':').Append(c.IsNullable).Append(':').Append(c.MaxLength).Append(';');
                    }
                    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))).Substring(0, 16);
                }

                var udtts = udttModels.Select(u => new SnapshotUdtt
                {
                    Schema = u.Schema,
                    Name = u.Name,
                    UserTypeId = u.Id,
                    Columns = u.Columns.Select(c => new SnapshotUdttColumn
                    {
                        Name = c.Name,
                        SqlTypeName = c.SqlTypeName,
                        IsNullable = c.IsNullable,
                        MaxLength = c.MaxLength
                    }).ToList(),
                    Hash = HashUdtt(u.Schema, u.Name, u.Columns)
                }).ToList();

                // TableType refs per schema
                var schemaSnapshots = schemas.Select(sc => new SnapshotSchema
                {
                    Name = sc.Name,
                    TableTypeRefs = udtts.Where(u => u.Schema.Equals(sc.Name, StringComparison.OrdinalIgnoreCase)).Select(u => $"{u.Schema}.{u.Name}").OrderBy(x => x).ToList()
                }).ToList();

                // Derive DB identity (best-effort parse of connection string)
                string serverName = null, databaseName = null;
                try
                {
                    var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(config.Project.DataBase.ConnectionString);
                    serverName = builder.DataSource;
                    databaseName = builder.InitialCatalog;
                }
                catch { }

                var fingerprint = schemaSnapshotService.BuildFingerprint(serverName, databaseName, buildSchemas, procedures.Count, udtts.Count, parserVersion: 5);
                var serverHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(serverName ?? "?"))).Substring(0, 16);

                var snapshot = new SchemaSnapshot
                {
                    Fingerprint = fingerprint,
                    Database = new SnapshotDatabase { ServerHash = serverHash, Name = databaseName },
                    Procedures = procedures,
                    Schemas = schemaSnapshots,
                    UserDefinedTableTypes = udtts,
                    Parser = new SnapshotParserInfo { ToolVersion = config.TargetFramework, ResultSetParserVersion = 5 },
                    Stats = new SnapshotStats
                    {
                        ProcedureTotal = procedures.Count,
                        ProcedureLoaded = procedures.Count(p => p.ResultSets != null && p.ResultSets.Count > 0),
                        ProcedureSkipped = procedures.Count(p => p.ResultSets == null || p.ResultSets.Count == 0),
                        UdttTotal = udtts.Count
                    }
                };

                schemaSnapshotService.Save(snapshot);
                consoleService.Verbose($"[snapshot] saved fingerprint={fingerprint} procs={procedures.Count} udtts={udtts.Count}");
                // Informational migration note (legacy schema node removed)
                consoleService.Verbose("[migration] Legacy 'schema' node removed; snapshot + IgnoredSchemas are now authoritative.");
                // Always show run-level JSON enrichment summary (even if zero) unless logging is Off
                if (jsonTypeLogLevel != JsonTypeLogLevel.Off)
                {
                    var summaryLine = $"[json-type-run-summary] procedures={procedures.Count(p => p.ResultSets.Any(rs => rs.Columns.Any()))} resolvedColumns={enrichmentStats.ResolvedColumns} new={enrichmentStats.NewConcrete} upgrades={enrichmentStats.Upgrades}";
                    if (options.Verbose) consoleService.Verbose(summaryLine); else consoleService.Output(summaryLine);
                }
            }
            catch (Exception sx)
            {
                consoleService.Verbose($"[snapshot-error] {sx.Message}");
            }
        }
        catch (SqlException sqlEx)
        {
            consoleService.Error($"Database error while retrieving schemas: {sqlEx.Message}");
            if (options.Verbose)
            {
                consoleService.Error(sqlEx.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
        catch (Exception ex)
        {
            consoleService.Error($"Error while retrieving schemas: {ex.Message}");
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }

        var pullSchemas = schemas.Where(x => x.Status == SchemaStatusEnum.Build);
        var ignoreSchemas = schemas.Where(x => x.Status == SchemaStatusEnum.Ignore);

        var pulledStoredProcedures = pullSchemas.SelectMany(x => x.StoredProcedures ?? []).ToList();

        var pulledSchemasWithStoredProcedures = pullSchemas
            .Select(x => new
            {
                Schema = x,
                StoredProcedures = x.StoredProcedures?.ToList()
            }).ToList();

        // Removed per new logging scheme (proc-loaded / proc-skip now emitted in SchemaManager)
        consoleService.Verbose("[info] Stored procedure enumeration complete (detailed load logs shown earlier)");
        consoleService.Output("");

        var ignoreSchemasCount = ignoreSchemas.Count();
        if (ignoreSchemasCount > 0)
        {
            consoleService.Warn($"Ignored {ignoreSchemasCount} Schemas [{string.Join(", ", ignoreSchemas.Select(x => x.Name))}]");
            consoleService.Output("");
        }

        consoleService.Info($"Pulled {pulledStoredProcedures.Count} StoredProcedures from {pullSchemas.Count()} Schemas [{string.Join(", ", pullSchemas.Select(x => x.Name))}] in {stopwatch.ElapsedMilliseconds} ms.");
        consoleService.Output("");

        if (options.DryRun)
        {
            consoleService.PrintDryRunMessage();
        }
        else
        {
            // Persist IgnoredSchemas changes if SchemaManager added or promoted schemas
            var currentIgnored = config.Project?.IgnoredSchemas ?? new List<string>();
            bool ignoredChanged = currentIgnored.Count != originalIgnored.Count || currentIgnored.Any(i => !originalIgnored.Contains(i));
            if (ignoredChanged)
            {
                try
                {
                    await configFile.SaveAsync(config);
                    consoleService.Verbose("[ignore] Persisted updated IgnoredSchemas to configuration file");
                }
                catch (Exception sx)
                {
                    consoleService.Warn($"Failed to persist updated IgnoredSchemas: {sx.Message}");
                }
            }
        }

        return ExecuteResultEnum.Succeeded;
    }

    public async Task<ExecuteResultEnum> BuildAsync(ICommandOptions options)
    {
        if (!configFile.Exists())
        {
            consoleService.Error("Configuration file not found");
            consoleService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
            return ExecuteResultEnum.Error;
        }

        await RunAutoUpdateAsync(options);

        if (await RunConfigVersionCheckAsync(options) == ExecuteResultEnum.Aborted)
            return ExecuteResultEnum.Aborted;

        consoleService.PrintTitle($"Build DataContext from {Constants.ConfigurationFile}");

        // Reuse unified configuration loading + user overrides
        var mergedConfig = await LoadAndMergeConfigurationsAsync();
        if (mergedConfig?.Project == null)
        {
            consoleService.Error("Configuration is invalid (project node missing)");
            return ExecuteResultEnum.Error;
        }

        // Propagate merged configuration to shared configFile so generators (TemplateManager, OutputService) see updated values like Project.Output.Namespace
        try
        {
            configFile.OverwriteWithConfig = mergedConfig;
            // Force refresh of cached Config instance
            await configFile.ReloadAsync();
        }
        catch (Exception ex)
        {
            consoleService.Warn($"Could not propagate merged configuration to FileManager: {ex.Message}");
        }

        var project = mergedConfig.Project;
        var connectionString = project?.DataBase?.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            consoleService.Error("Missing database connection string");
            consoleService.Output($"\tAdd it to {Constants.ConfigurationFile} (Project.DataBase.ConnectionString) or run '{Constants.Name} set --cs \"your-connection-string\"'.");
            return ExecuteResultEnum.Error;
        }
        if (options.Verbose)
        {
            consoleService.Verbose($"[build] Using connection string (length={connectionString?.Length}) from configuration.");
        }

        try
        {
            dbContext.SetConnectionString(connectionString);
            // Ensure snapshot presence (required for metadata-driven generation) – but still require connection now (no offline mode)
            try
            {
                var working = Utils.DirectoryUtils.GetWorkingDirectory();
                var schemaDir = System.IO.Path.Combine(working, ".spocr", "schema");
                if (!System.IO.Directory.Exists(schemaDir) || System.IO.Directory.GetFiles(schemaDir, "*.json").Length == 0)
                {
                    consoleService.Error("No snapshot found. Run 'spocr pull' before 'spocr build'.");
                    consoleService.Output($"\tAlternative: 'spocr rebuild{(string.IsNullOrWhiteSpace(options.Path) ? string.Empty : " -p " + options.Path)}' führt Pull + Build in einem Schritt aus.");
                    return ExecuteResultEnum.Error;
                }
            }
            catch (Exception)
            {
                consoleService.Error("Unable to verify snapshot presence.");
                return ExecuteResultEnum.Error;
            }

            // Optional informational warning if legacy schema section still present (non-empty) – snapshot only build.
            if (mergedConfig.Schema != null && mergedConfig.Schema.Count > 0 && options.Verbose)
            {
                consoleService.Verbose("[legacy-schema] config.Schema is ignored (snapshot-only build mode)");
            }

            var elapsed = await GenerateCodeAsync(project, options);

            // Derive generator success/failure/skipped metrics from elapsed dictionary
            // Convention: If a generator type was enabled but produced 0 ms, treat as skipped.
            // (Future: we could carry explicit status objects instead of inferring.)
            var enabledGenerators = elapsed.Keys.ToList();
            var succeeded = elapsed.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
            var skipped = elapsed.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();

            var summaryLines = new List<string>();
            foreach (var kv in elapsed)
            {
                summaryLines.Add($"{kv.Key} generated in {kv.Value} ms.");
            }

            summaryLines.Add("");
            summaryLines.Add($"Generators succeeded: {succeeded.Count}/{enabledGenerators.Count} [{string.Join(", ", succeeded)}]");
            if (skipped.Count > 0)
            {
                summaryLines.Add($"Generators skipped (0 changes): {skipped.Count} [{string.Join(", ", skipped)}]");
            }

            consoleService.PrintSummary(summaryLines, $"{Constants.Name} v{service.Version.ToVersionString()}");

            var totalElapsed = elapsed.Values.Sum();
            consoleService.PrintTotal($"Total elapsed time: {totalElapsed} ms.");

            if (options.DryRun)
            {
                consoleService.PrintDryRunMessage();
            }

            return ExecuteResultEnum.Succeeded;
        }
        catch (SqlException sqlEx)
        {
            consoleService.Error($"Database error during the build process: {sqlEx.Message}");
            if (options.Verbose)
            {
                consoleService.Error(sqlEx.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
        catch (Exception ex)
        {
            consoleService.Error($"Unexpected error during the build process: {ex.Message}");
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
    }

    public async Task<ExecuteResultEnum> RemoveAsync(ICommandOptions options)
    {
        if (!configFile.Exists())
        {
            consoleService.Error("Configuration file not found");
            consoleService.Output($"\tNothing to remove.");
            return ExecuteResultEnum.Error;
        }

        await RunAutoUpdateAsync(options);

        if (options.DryRun)
        {
            consoleService.PrintDryRunMessage("Would remove all generated files");
            return ExecuteResultEnum.Succeeded;
        }

        var proceed1 = consoleService.GetYesNo("Remove all generated files?", true);
        if (!proceed1) return ExecuteResultEnum.Aborted;

        try
        {
            output.RemoveGeneratedFiles(configFile.Config.Project.Output.DataContext.Path, options.DryRun);
            consoleService.Output("Generated folder and files removed.");
        }
        catch (Exception ex)
        {
            consoleService.Error($"Failed to remove files: {ex.Message}");
            return ExecuteResultEnum.Error;
        }

        var proceed2 = consoleService.GetYesNo($"Remove {Constants.ConfigurationFile}?", true);
        if (!proceed2) return ExecuteResultEnum.Aborted;

        await configFile.RemoveAsync(options.DryRun);
        consoleService.Output($"{Constants.ConfigurationFile} removed.");

        return ExecuteResultEnum.Succeeded;
    }

    public async Task<ExecuteResultEnum> GetVersionAsync()
    {
        var current = service.Version;
        var latest = await autoUpdaterService.GetLatestVersionAsync();

        consoleService.Output($"Version: {current.ToVersionString()}");

        if (current.IsGreaterThan(latest))
            consoleService.Output($"Latest:  {latest?.ToVersionString()} (Development build)");
        else
            consoleService.Output($"Latest:  {latest?.ToVersionString() ?? current.ToVersionString()}");

        return ExecuteResultEnum.Succeeded;
    }

    public async Task ReloadConfigurationAsync()
    {
        await configFile.ReloadAsync();
    }

    private bool _versionCheckPerformed = false;
    private async Task<ExecuteResultEnum> RunConfigVersionCheckAsync(ICommandOptions options)
    {
        if (options.NoVersionCheck || _versionCheckPerformed) return ExecuteResultEnum.Skipped;
        _versionCheckPerformed = true;

        var check = configFile.CheckVersion();
        if (!check.DoesMatch)
        {
            if (check.SpocRVersion.IsGreaterThan(check.ConfigVersion))
            {
                consoleService.Warn($"Your local {Constants.ConfigurationFile} Version {check.SpocRVersion.ToVersionString()} is greater than the {Constants.ConfigurationFile} Version {check.ConfigVersion.ToVersionString()}");
                var answer = consoleService.GetSelectionMultiline("Do you want to continue?", ["Continue", "Cancel"]);
                if (answer.Value != "Continue")
                {
                    return ExecuteResultEnum.Aborted;
                }
            }
            else if (check.SpocRVersion.IsLessThan(check.ConfigVersion))
            {
                consoleService.Warn($"Your local {Constants.ConfigurationFile} Version {check.SpocRVersion.ToVersionString()} is lower than the {Constants.ConfigurationFile} Version {check.ConfigVersion.ToVersionString()}");
                var latestVersion = await autoUpdaterService.GetLatestVersionAsync();
                var answer = consoleService.GetSelectionMultiline("Do you want to continue?", ["Continue", "Cancel", $"Update {Constants.Name} to {latestVersion}"]);
                switch (answer.Value)
                {
                    case "Update":
                        autoUpdaterService.InstallUpdate();
                        break;
                    case "Continue":
                        break;
                    default:
                        return ExecuteResultEnum.Aborted;
                }
            }
        }

        return ExecuteResultEnum.Succeeded;
    }

    private async Task RunAutoUpdateAsync(ICommandOptions options)
    {
        if (options.NoAutoUpdate)
        {
            consoleService.Verbose("Auto-update skipped via --no-auto-update flag");
            return;
        }

        // Environment variable guard (mirrors service internal check for early exit)
        if (Environment.GetEnvironmentVariable("SPOCR_SKIP_UPDATE")?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" ||
            Environment.GetEnvironmentVariable("SPOCR_NO_UPDATE")?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on")
        {
            consoleService.Verbose("Auto-update skipped via environment variable before invoking service");
            return;
        }

        if (!options.Quiet)
        {
            try
            {
                await autoUpdaterService.RunAsync();
            }
            catch (Exception ex)
            {
                consoleService.Warn($"Auto-update check failed: {ex.Message}");
            }
        }
    }

    private Task<Dictionary<string, long>> GenerateCodeAsync(ProjectModel project, ICommandOptions options)
    {
        try
        {
            if (options is IBuildCommandOptions buildOptions && buildOptions.GeneratorTypes != GeneratorTypes.All)
            {
                orchestrator.EnabledGeneratorTypes = buildOptions.GeneratorTypes;
                consoleService.Verbose($"Generator types restricted to: {buildOptions.GeneratorTypes}");
            }

            // Zugriff weiterhin nötig bis vollständige Entfernung in v5 – Obsolete Warning lokal unterdrücken
#pragma warning disable CS0618
            return orchestrator.GenerateCodeWithProgressAsync(options.DryRun, project.Role.Kind, project.Output);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace);
            }
            else
            {
                consoleService.Output("\tRun with --verbose for more details");
            }

            return Task.FromResult(new Dictionary<string, long>());
        }
    }

    private async Task<ConfigurationModel> LoadAndMergeConfigurationsAsync()
    {
        var config = await configFile.ReadAsync();
        if (config == null)
        {
            consoleService.Error("Failed to read configuration file");
            return new ConfigurationModel();
        }

        // Migration / Normalisierung Role (Deprecation Path)
        try
        {
            // Falls Role fehlt => Standard auffüllen (Kind=Default)
            if (config.Project != null && config.Project.Role == null)
            {
                config.Project.Role = new RoleModel();
            }
            // Deprecation Warnung wenn ein Nicht-Default Wert gesetzt ist
            // Deprecation Warning nur wenn alter Wert (Lib/Extension) verwendet wird
#pragma warning disable CS0618
            if (config.Project?.Role?.Kind is SpocR.Enums.RoleKindEnum.Lib or SpocR.Enums.RoleKindEnum.Extension)
#pragma warning restore CS0618
            {
                consoleService.Warn("[deprecation] Project.Role.Kind is deprecated and will be removed in v5. Remove the 'Role' section or set it to Default. See migration guide.");
            }
        }
        catch (Exception ex)
        {
            consoleService.Verbose($"[role-migration-warn] {ex.Message}");
        }

        var userConfigFileName = Constants.UserConfigurationFile.Replace("{userId}", globalConfigFile.Config?.UserId);
        if (string.IsNullOrEmpty(globalConfigFile.Config?.UserId))
        {
            consoleService.Verbose("No user ID found in global configuration");
            return config;
        }

        var userConfigFile = new FileManager<ConfigurationModel>(service, userConfigFileName);

        if (userConfigFile.Exists())
        {
            consoleService.Verbose($"Merging user configuration from {userConfigFileName}");
            var userConfig = await userConfigFile.ReadAsync();
            if (userConfig != null)
            {
                return config.OverwriteWith(userConfig);
            }
            else
            {
                consoleService.Warn($"User configuration file exists but could not be read: {userConfigFileName}");
            }
        }

        return config;
    }
}

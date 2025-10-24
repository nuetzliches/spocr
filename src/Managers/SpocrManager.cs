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
using SpocR.Services;
using SpocR.Models;
using SpocR.DataContext.Queries;
using SpocR.DataContext.Models;
using SpocR.SpocRVNext.Engine; // vNext template engine
using SpocR.SpocRVNext; // dispatcher & generator
using SpocRVNext.Configuration; // EnvConfiguration
using SpocRVNext.Metadata; // vNext TableType metadata provider
using SpocR.SpocRVNext.Generators; // vNext TableTypesGenerator

namespace SpocR.Managers;

// Hilfs-Zeilenmodelle für generisches Mapping von Tabellen- und View-Namen
internal class TableNameRow
{
    public string schema_name { get; set; }
    public string table_name { get; set; }
}

internal class ViewNameRow
{
    public string schema_name { get; set; }
    public string view_name { get; set; }
}

public class SpocrManager(
    SpocrService service,
    OutputService output,
    CodeGenerationOrchestrator orchestrator,
    SpocrProjectManager projectManager,
    IConsoleService consoleService,
    SchemaManager schemaManager,
    Services.SchemaSnapshotFileLayoutService expandedSnapshotService,
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
        // Role deprecated – only display options as migration notice if user explicitly provides a value
        var roleKindString = options.Role;
        RoleKindEnum roleKind = RoleKindEnum.Default; // Always set Default
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
            // Bridge: allow .env-only workflow in dual/next mode if SPOCR_GENERATOR_DB present
            var genMode = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE")?.Trim().ToLowerInvariant();
            var envConn = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_DB");
            if (genMode is "dual" or "next" && !string.IsNullOrWhiteSpace(envConn))
            {
                consoleService.Warn("[bridge] spocr.json missing – proceeding using .env (SPOCR_GENERATOR_DB). Some legacy-only features unavailable.");
                // Minimal config stub for downstream code expecting non-null
                var stub = new ConfigurationModel
                {
                    Version = new Version(4, 0, 0, 0),
                    TargetFramework = Constants.DefaultTargetFramework.ToFrameworkString(),
                    Project = new ProjectModel
                    {
                        DataBase = new DataBaseModel { ConnectionString = envConn },
                        Output = new OutputModel { Namespace = Environment.GetEnvironmentVariable("SPOCR_NAMESPACE") ?? "SpocR" }
                    },
                    Schema = new List<SchemaModel>()
                };
                // Use in-memory path: configFile not saved (skip save operations later)
                dbContext.SetConnectionString(envConn);
                consoleService.PrintTitle("Pulling database schema from database");
            }
            else
            {
                consoleService.Error("Configuration file not found");
                consoleService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
                return ExecuteResultEnum.Error;
            }
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
                                    // removed flag
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
                // Dedupe-Strukturen für JSON Typing Logs (verhindert mehrfaches Rauschen gleicher Spalte)
                var jsonTypingLogSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Stack used to build DotPath names during recursive mapping (initialized before mapping).
                // Removed DotPath stacking: Name now strictly equals SQL alias as parsed (reverted per requirements)
                var _columnNameStack = new Stack<string>(); // retained only for possible future diagnostics; not used for naming
                // Local helper to map runtime ResultColumn (flattened JSON model) -> snapshot column recursively
                // Rein strukturelle Typinferenz aus RawExpression (Aggregat-/CASE Muster) – keine Namensheuristiken.
                string InferSqlTypeFromRawExpression(string raw, bool hasIntLit, bool hasDecLit)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return null;
                    // Normalisieren für Pattern Checks
                    var lower = raw.ToLowerInvariant();
                    // Entferne überflüssige Whitespaces für kompakte Contains-Prüfungen
                    lower = System.Text.RegularExpressions.Regex.Replace(lower, "\n+", " ");
                    // Aggregat-Erkennung
                    bool hasSum = lower.Contains("sum(");
                    bool hasCountBig = lower.Contains("count_big(");
                    bool hasCount = lower.Contains("count(") && !hasCountBig; // count_big schon abgedeckt
                    bool hasAvg = lower.Contains("avg(");
                    bool hasExists = lower.Contains("exists(");
                    bool hasIif = lower.Contains("iif(");
                    // CASE 1/0 Pattern (rein boolesch)
                    bool caseZeroOne = lower.StartsWith("case") &&
                        ((lower.Contains(" then 1") && lower.Contains(" else 0")) || (lower.Contains(" then 0") && lower.Contains(" else 1")));

                    if (hasCountBig) return "bigint";
                    if (hasCount) return "int";
                    if (hasAvg) return "decimal(18,2)";
                    if (hasExists) return "bit"; // EXISTS(SELECT ...) boolescher Ausdruck
                    if (hasIif)
                    {
                        // IIF(...,1,0) oder IIF(...,0,1) -> bit (rein boolesch)
                        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"iif\s*\([^,]+,\s*1\s*,\s*0\s*\)")
                            || System.Text.RegularExpressions.Regex.IsMatch(lower, @"iif\s*\([^,]+,\s*0\s*,\s*1\s*\)"))
                        {
                            return "bit";
                        }
                    }
                    if (hasSum)
                    {
                        // SUM über reinem 0/1 Muster (IIF / CASE) → int
                        if ((lower.Contains("iif(") && lower.Contains(",1,0")) || (lower.Contains(" then 1") && lower.Contains(" else 0"))) return "int";
                        if (hasDecLit) return "decimal(18,2)";
                        if (hasIntLit) return "int";
                        // Konservativer Default für SUM ohne Literale (häufig monetär)
                        return "decimal(18,2)";
                    }
                    if (caseZeroOne) return "bit"; // reine boolesche Ableitung
                    return null; // kein Muster erkannt → offen lassen
                }

                string ExtractLeadingFunctionName(string raw)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return null;
                    var m = System.Text.RegularExpressions.Regex.Match(raw, @"^\s*([A-Za-z0-9_]+)\s*\(");
                    return m.Success ? m.Groups[1].Value : null;
                }

                SnapshotResultColumn MapSnapshotResultColumn(StoredProcedureContentModel.ResultColumn col, bool parentReturnsJson, StoredProcedureModel spCtx)
                {
                    var snap = new SnapshotResultColumn
                    {
                        Name = col.Name,
                        SqlTypeName = ShouldPruneSqlType(col.SqlTypeName, parentReturnsJson) ? null : col.SqlTypeName,
                        IsNullable = col.IsNullable,
                        MaxLength = col.MaxLength,
                        UserTypeSchemaName = col.UserTypeSchemaName,
                        UserTypeName = col.UserTypeName,
                        IsNestedJson = (col.IsNestedJson == true && col.ReturnsJson != true) ? true : null,
                        ReturnsJson = col.ReturnsJson,
                        ReturnsJsonArray = col.ReturnsJsonArray,
                        JsonRootProperty = col.JsonRootProperty
                    };
                    // CAST / CONVERT Zieltyp Übernahme falls aktueller Typ leer/ngepruned
                    if (snap.SqlTypeName == null && !string.IsNullOrWhiteSpace(col.CastTargetType))
                    {
                        var castType = col.CastTargetType.Trim();
                        // Parameterisierte Typen zusammensetzen
                        if (col.CastTargetPrecision.HasValue && col.CastTargetScale.HasValue && (castType.Equals("decimal", StringComparison.OrdinalIgnoreCase) || castType.Equals("numeric", StringComparison.OrdinalIgnoreCase)))
                        {
                            snap.SqlTypeName = $"{castType}({col.CastTargetPrecision.Value},{col.CastTargetScale.Value})";
                        }
                        else if (col.CastTargetLength.HasValue && (castType.Contains("char", StringComparison.OrdinalIgnoreCase) || castType.Contains("binary", StringComparison.OrdinalIgnoreCase)))
                        {
                            snap.SqlTypeName = $"{castType}({col.CastTargetLength.Value})";
                        }
                        else
                        {
                            snap.SqlTypeName = castType;
                        }
                    }
                    // Zusätzliche Typinferenz für JSON Columns ohne konkrete Quelle
                    if (parentReturnsJson && string.IsNullOrWhiteSpace(snap.SqlTypeName))
                    {
                        var fnName = col.AggregateFunction;
                        if (string.IsNullOrWhiteSpace(fnName) && !string.IsNullOrWhiteSpace(col.RawExpression) && col.ExpressionKind == StoredProcedureContentModel.ResultColumnExpressionKind.FunctionCall)
                            fnName = ExtractLeadingFunctionName(col.RawExpression);
                        if (!string.IsNullOrWhiteSpace(fnName))
                        {
                            var fnLower = fnName.ToLowerInvariant();
                            switch (fnLower)
                            {
                                case "count":
                                    snap.SqlTypeName = "int"; break;
                                case "count_big":
                                    snap.SqlTypeName = "bigint"; break; // korrekt für COUNT_BIG
                                case "sum":
                                    var rawExpr = col.RawExpression?.Trim();
                                    if (!string.IsNullOrWhiteSpace(rawExpr))
                                    {
                                        var rawLower = rawExpr.ToLowerInvariant();
                                        if (rawLower.Contains("iif") && rawLower.Contains(",1,0")) { snap.SqlTypeName = "int"; break; }
                                        if (rawLower.StartsWith("sum(") && rawLower.Contains(" then 1") && rawLower.Contains(" else 0")) { snap.SqlTypeName = "int"; break; }
                                    }
                                    if (snap.SqlTypeName == null)
                                    {
                                        if (col.HasDecimalLiteral) snap.SqlTypeName = "decimal(18,2)"; else if (col.HasIntegerLiteral) snap.SqlTypeName = "int"; else snap.SqlTypeName = "decimal(18,2)";
                                    }
                                    break;
                                case "avg":
                                    snap.SqlTypeName = "decimal(18,2)"; break;
                                case "exists":
                                    snap.SqlTypeName = "bit"; break;
                                case "min":
                                case "max":
                                    break;
                            }
                            if (options.Verbose && parentReturnsJson)
                            {
                                if (snap.SqlTypeName != null)
                                    consoleService.Verbose($"[json-agg-diag] innerSubqueryResolved fn={fnLower} name={col.Name} sqlType={snap.SqlTypeName}");
                                else
                                    consoleService.Verbose($"[json-agg-diag] unresolved-agg fn={fnLower} name={col.Name} hasIntLit={col.HasIntegerLiteral} hasDecLit={col.HasDecimalLiteral} raw='{col.RawExpression?.Replace("\n", " ")}'");
                            }
                        }
                        if (string.IsNullOrWhiteSpace(snap.SqlTypeName) && !string.IsNullOrWhiteSpace(col.RawExpression))
                        {
                            var raw = col.RawExpression.Trim();
                            if (raw.StartsWith("CASE", StringComparison.OrdinalIgnoreCase))
                            {
                                if (raw.IndexOf(" THEN 1", StringComparison.OrdinalIgnoreCase) > 0 && raw.IndexOf(" ELSE 0", StringComparison.OrdinalIgnoreCase) > 0)
                                    snap.SqlTypeName = "int";
                                else if (raw.IndexOf(" THEN 0", StringComparison.OrdinalIgnoreCase) > 0 && raw.IndexOf(" ELSE 1", StringComparison.OrdinalIgnoreCase) > 0)
                                    snap.SqlTypeName = "int";
                            }
                        }
                    }

                    // NEU: Aggregat-Typableitung auch für expandierte Kindspalten (unabhängig von parentReturnsJson), falls noch kein Typ gesetzt
                    if (string.IsNullOrWhiteSpace(snap.SqlTypeName) && col.IsAggregate == true && !string.IsNullOrWhiteSpace(col.AggregateFunction))
                    {
                        var ag = col.AggregateFunction.ToLowerInvariant();
                        switch (ag)
                        {
                            case "count":
                                snap.SqlTypeName = "int"; break;
                            case "count_big":
                                snap.SqlTypeName = "bigint"; break;
                            case "sum":
                                if (col.HasDecimalLiteral) snap.SqlTypeName = "decimal(18,2)"; else if (col.HasIntegerLiteral) snap.SqlTypeName = "int"; else snap.SqlTypeName = "decimal(18,2)"; break;
                            case "avg":
                                snap.SqlTypeName = "decimal(18,2)"; break;
                            case "exists":
                                snap.SqlTypeName = "bit"; break;
                            case "min":
                            case "max":
                                // Offen lassen – ohne Quelltyp nicht deterministisch
                                break;
                        }
                        if (options.Verbose && snap.SqlTypeName != null)
                        {
                            consoleService.Verbose($"[json-agg-diag] aggregate-child-resolved name={snap.Name} aggFn={ag} sqlType={snap.SqlTypeName}");
                        }
                    }
                    // NEU: Computed-Ausdruck ohne Aggregatfunktion aber ausschließlich Integer-Kontext (z.B. SUM(...) + SUM(...))
                    // Beide Operanden waren Aggregat-Spalten mit HasIntegerLiteral propagiert -> resultierende BinaryExpression bekommt HasIntegerLiteral=true, HasDecimalLiteral=false.
                    // Wir inferieren daraus int, sofern kein Decimal-Literal Flag gesetzt wurde.
                    if (string.IsNullOrWhiteSpace(snap.SqlTypeName)
                        && col.IsAggregate != true
                        && col.ExpressionKind == StoredProcedureContentModel.ResultColumnExpressionKind.Computed
                        && col.HasIntegerLiteral
                        && !col.HasDecimalLiteral)
                    {
                        snap.SqlTypeName = "int";
                        if (options.Verbose)
                        {
                            consoleService.Verbose($"[json-agg-diag] computed-inferred-int name={snap.Name} hasIntLit={col.HasIntegerLiteral} hasDecLit={col.HasDecimalLiteral}");
                        }
                    }
                    // Falls ein Computed-Ausdruck Dezimal-Literale enthält (z.B. SUM(...) * 1.0) → decimal(18,2)
                    if (string.IsNullOrWhiteSpace(snap.SqlTypeName)
                        && col.IsAggregate != true
                        && col.ExpressionKind == StoredProcedureContentModel.ResultColumnExpressionKind.Computed
                        && col.HasDecimalLiteral)
                    {
                        snap.SqlTypeName = "decimal(18,2)";
                        if (options.Verbose)
                        {
                            consoleService.Verbose($"[json-agg-diag] computed-inferred-decimal name={snap.Name} hasIntLit={col.HasIntegerLiteral} hasDecLit={col.HasDecimalLiteral}");
                        }
                    }
                    // Zusätzliche strukturelle Ableitung auf Basis des RawExpression-Inhalts (nur wenn bisher kein Typ ermittelt wurde)
                    if (string.IsNullOrWhiteSpace(snap.SqlTypeName) && parentReturnsJson && !string.IsNullOrWhiteSpace(col.RawExpression))
                    {
                        var inferredRaw = InferSqlTypeFromRawExpression(col.RawExpression, col.HasIntegerLiteral, col.HasDecimalLiteral);
                        if (!string.IsNullOrWhiteSpace(inferredRaw))
                        {
                            snap.SqlTypeName = inferredRaw;
                            if (options.Verbose)
                            {
                                var trimmed = col.RawExpression.Length > 120 ? col.RawExpression.Substring(0, 117) + "..." : col.RawExpression;
                                consoleService.Verbose($"[json-agg-diag] rawexpr-infer name={snap.Name} type={snap.SqlTypeName} raw='{trimmed.Replace("\n", " ")}'");
                            }
                        }
                    }
                    // StoredProcedureModel verwendet Property 'Input' (List<StoredProcedureInputModel>). Zugriff ohne Methodengruppe Absicherung.
                    var spInputs = spCtx.Input?.ToList();
                    if (snap.SqlTypeName == null && parentReturnsJson && !string.IsNullOrWhiteSpace(snap.Name) && spInputs != null && spInputs.Count() > 0)
                    {
                        if (snap.Name.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                        {
                            var paramName = snap.Name.Substring(7);
                            var match = spInputs.FirstOrDefault(i => i.Name?.TrimStart('@').Equals(paramName, StringComparison.OrdinalIgnoreCase) == true);
                            if (match != null && !string.IsNullOrWhiteSpace(match.SqlTypeName))
                            {
                                snap.SqlTypeName = match.SqlTypeName;
                                if (match.IsNullable.HasValue) snap.IsNullable = match.IsNullable.Value ? true : null;
                                if (match.MaxLength.HasValue && match.MaxLength.Value > 0) snap.MaxLength = match.MaxLength.Value;
                            }
                        }
                    }
                    if (options.Verbose && parentReturnsJson && snap.SqlTypeName == null)
                    {
                        // Key = Proc + Column + Tag
                        string procKey = spCtx?.SchemaName + "." + spCtx?.Name;
                        bool isContainer = (col.ReturnsJson == true || col.IsNestedJson == true) && col.Columns != null && col.Columns.Count > 0;
                        string tag = isContainer ? "json-container" : "unresolved-json-column";
                        string key = procKey + "|" + snap.Name + "|" + tag;
                        if (jsonTypingLogSeen.Add(key))
                        {
                            if (isContainer)
                            {
                                consoleService.Verbose($"[json-agg-diag] json-container name={snap.Name} childCount={col.Columns.Count}");
                            }
                            else
                            {
                                consoleService.Verbose($"[json-agg-diag] unresolved-json-column name={snap.Name} exprKind={col.ExpressionKind} aggFn={col.AggregateFunction} isAgg={col.IsAggregate} hasIntLit={col.HasIntegerLiteral} hasDecLit={col.HasDecimalLiteral}");
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(snap.UserTypeName))
                    {
                        snap.SqlTypeName = null; snap.IsNullable = null; snap.MaxLength = null;
                    }
                    if (snap.IsNullable == false) snap.IsNullable = null;
                    if ((col.IsNestedJson == true || col.ReturnsJson == true) && col.Columns != null && col.Columns.Count > 0)
                    {
                        _columnNameStack.Push(col.Name);
                        snap.Columns = col.Columns.Select(n => MapSnapshotResultColumn(n, col.ReturnsJson == true, spCtx)).Where(c => c != null).ToList();
                        _columnNameStack.Pop();
                    }
                    else { snap.Columns = null; }
                    snap.Reference = col.Reference != null ? new SnapshotColumnReference { Kind = col.Reference.Kind, Schema = col.Reference.Schema, Name = col.Reference.Name } : null;
                    snap.DeferredJsonExpansion = col.DeferredJsonExpansion == true ? true : null;
                    return snap;
                }

                var procedures = schemas.SelectMany(sc => sc.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>())
                    .Select(sp =>
                    {
                        var rsRaw = (sp.Content?.ResultSets ?? Array.Empty<StoredProcedureContentModel.ResultSet>());
                        // Remove placeholder empty entries (no columns, no JSON flags) to avoid fake ResultSets (e.g. BannerDelete)
                        // BUT preserve pure wrapper reference sets (ExecSourceProcedureName set) even if they have no columns and no JSON flags.
                        var rsFiltered = rsRaw.Where(r =>
                            r.ReturnsJson ||
                            r.ReturnsJsonArray ||
                            (r.Columns?.Count > 0) ||
                            !string.IsNullOrEmpty(r.ExecSourceProcedureName)
                        ).ToArray();
                        return new SnapshotProcedure
                        {
                            Schema = sp.SchemaName,
                            Name = sp.Name,
                            Inputs = (sp.Input ?? []).Select(i => new SnapshotInput
                            {
                                Name = i.Name?.TrimStart('@'),
                                TableTypeSchema = i.TableTypeSchemaName,
                                TableTypeName = i.TableTypeName,
                                IsOutput = i.IsOutput ? true : null,
                                SqlTypeName = i.SqlTypeName,
                                IsNullable = (i.IsNullable ?? false) ? true : null,
                                MaxLength = (i.MaxLength.HasValue && i.MaxLength.Value > 0) ? i.MaxLength.Value : null,
                                // HasDefaultValue derzeit nicht im StoredProcedureInputModel verfügbar (nur Function Parameters Flag) → ausgelassen
                            }).ToList(),
                            ResultSets = rsFiltered.Select(rs => new SnapshotResultSet
                            {
                                ReturnsJson = rs.ReturnsJson,
                                ReturnsJsonArray = rs.ReturnsJsonArray,
                                JsonRootProperty = rs.JsonRootProperty,
                                ExecSourceSchemaName = rs.ExecSourceSchemaName,
                                ExecSourceProcedureName = rs.ExecSourceProcedureName,
                                HasSelectStar = rs.HasSelectStar == true ? true : false,
                                Reference = rs.Reference != null ? new SnapshotColumnReference { Kind = rs.Reference.Kind, Schema = rs.Reference.Schema, Name = rs.Reference.Name } : null,
                                Columns = rs.Columns.Select(c => MapSnapshotResultColumn(c, rs.ReturnsJson, sp)).ToList()
                            }).ToList()
                        };
                    }).ToList();

                bool ShouldPruneSqlType(string sqlType, bool parentJson)
                {
                    if (string.IsNullOrWhiteSpace(sqlType)) return parentJson; // leer im JSON Kontext prunen
                    var t = sqlType.Trim().ToLowerInvariant();
                    if (!parentJson) return false; // außerhalb JSON nie prunen
                    // Fallback Kandidaten: nvarchar(max), unknown
                    if (t == "nvarchar(max)" || t == "unknown") return true;
                    return false; // konkrete Typen behalten
                }

                // (Reverted) Keine automatische Stub-Erzeugung für cross-schema EXEC Ziele – ursprüngliches Verhalten wiederhergestellt.

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
                        IsNullable = c.IsNullable ? true : null,
                        MaxLength = c.MaxLength > 0 ? c.MaxLength : null,
                        UserTypeSchemaName = string.IsNullOrWhiteSpace(c.UserTypeName) ? null : c.UserTypeSchemaName,
                        UserTypeName = string.IsNullOrWhiteSpace(c.UserTypeName) ? null : c.UserTypeName,
                        BaseSqlTypeName = (!string.IsNullOrWhiteSpace(c.BaseSqlTypeName) && !string.Equals(c.BaseSqlTypeName, c.SqlTypeName, StringComparison.OrdinalIgnoreCase)) ? c.BaseSqlTypeName : null,
                        Precision = (c.Precision.HasValue && c.Precision.Value > 0) ? c.Precision : null,
                        Scale = (c.Scale.HasValue && c.Scale.Value > 0) ? c.Scale : null
                    }).ToList(),
                    Hash = HashUdtt(u.Schema, u.Name, u.Columns)
                }).ToList();

                // --- Artefakt-Lade-Reihenfolge (verbindlich):
                //   (A) UserDefinedTypes (skalare Alias-Typen)
                //   (B) Tables (benötigt für spätere Funktions-/JSON Typ-Anreicherung)
                //   (C) Views  (liefert fertige sys.columns Typen)
                //   (D) Functions (erst nach Tables/Views für sofortige korrekte Enrichment-Basis)
                //   (E) TableTypes (wurden bereits vorher geladen / UDTT für Procs)
                //   (F) Procedures (abschließende JSON/ResultSet Enrichment Phase)
                // Begründung Änderung: Funktions-Sammlung jetzt NACH Tabellen & Views, damit ColumnEnrichment nicht auf spätere Post-Phase angewiesen ist.
                bool phaseUserTypesDone = false, phaseTablesDone = false, phaseFunctionsDone = false, phaseProceduresDone = procedures.Count > 0;
                List<SnapshotFunction> collectedFunctions = null;

                var userDefinedTypeRows = await dbContext.UserDefinedScalarTypesAsync(System.Threading.CancellationToken.None);
                var userDefinedTypes = userDefinedTypeRows.Select(r => new SnapshotUserDefinedType
                {
                    Schema = r.schema_name,
                    Name = r.user_type_name,
                    BaseSqlTypeName = r.base_type_name,
                    MaxLength = r.max_length > 0 ? r.max_length : null,
                    Precision = r.precision > 0 ? r.precision : null,
                    Scale = r.scale > 0 ? r.scale : null,
                    IsNullable = null // Skalar UDT Nullability nicht separat ableitbar hier
                }).ToList();
                phaseUserTypesDone = true;

                // Entfernt: frühe Function-Sammlung (wird jetzt nach Tables & Views ausgeführt)

                // Helper für Spalten-Pruning (Tabellen & Views)
                SnapshotTableColumn MapTableColumn(Column c) => new SnapshotTableColumn
                {
                    Name = c.Name,
                    SqlTypeName = c.SqlTypeName,
                    IsNullable = c.IsNullable ? true : null, // false wird gepruned
                    MaxLength = c.MaxLength > 0 ? c.MaxLength : null,
                    IsIdentity = (c.IsIdentityRaw.HasValue && c.IsIdentityRaw.Value == 1) ? true : null,
                    UserTypeSchemaName = string.IsNullOrWhiteSpace(c.UserTypeName) ? null : c.UserTypeSchemaName,
                    UserTypeName = string.IsNullOrWhiteSpace(c.UserTypeName) ? null : c.UserTypeName,
                    BaseSqlTypeName = (!string.IsNullOrWhiteSpace(c.BaseSqlTypeName) && !string.Equals(c.BaseSqlTypeName, c.SqlTypeName, StringComparison.OrdinalIgnoreCase)) ? c.BaseSqlTypeName : null,
                    Precision = (c.Precision.HasValue && c.Precision.Value > 0) ? c.Precision : null,
                    Scale = (c.Scale.HasValue && c.Scale.Value > 0) ? c.Scale : null
                };

                SnapshotViewColumn MapViewColumn(Column c) => new SnapshotViewColumn
                {
                    Name = c.Name,
                    SqlTypeName = c.SqlTypeName,
                    IsNullable = c.IsNullable ? true : null,
                    MaxLength = c.MaxLength > 0 ? c.MaxLength : null,
                    UserTypeSchemaName = string.IsNullOrWhiteSpace(c.UserTypeName) ? null : c.UserTypeSchemaName,
                    UserTypeName = string.IsNullOrWhiteSpace(c.UserTypeName) ? null : c.UserTypeName,
                    BaseSqlTypeName = (!string.IsNullOrWhiteSpace(c.BaseSqlTypeName) && !string.Equals(c.BaseSqlTypeName, c.SqlTypeName, StringComparison.OrdinalIgnoreCase)) ? c.BaseSqlTypeName : null,
                    Precision = (c.Precision.HasValue && c.Precision.Value > 0) ? c.Precision : null,
                    Scale = (c.Scale.HasValue && c.Scale.Value > 0) ? c.Scale : null
                };

                // 2) Tabellen je Build-Schema
                var tables = new List<SnapshotTable>();
                if (buildSchemas.Count > 0)
                {
                    var schemaLiteral = string.Join(',', buildSchemas.Select(s => $"'{s}'"));
                    const string tableSql = @"SELECT s.name AS schema_name, t.name AS table_name
                                              FROM sys.tables AS t
                                              INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                                              WHERE s.name IN ({0})
                                              ORDER BY s.name, t.name"; // {0} wird unten ersetzt
                    var formattedTableSql = string.Format(tableSql, schemaLiteral);
                    var tableRows = await dbContext.ListAsync<TableNameRow>(formattedTableSql, new List<Microsoft.Data.SqlClient.SqlParameter>(), System.Threading.CancellationToken.None);
                    foreach (var row in tableRows)
                    {
                        var cols = await dbContext.TableColumnsListAsync(row.schema_name, row.table_name, System.Threading.CancellationToken.None);
                        tables.Add(new SnapshotTable
                        {
                            Schema = row.schema_name,
                            Name = row.table_name,
                            Columns = cols.Select(MapTableColumn).ToList()
                        });
                    }
                }
                phaseTablesDone = true;
                if (!phaseUserTypesDone) consoleService.Warn("[ordering-guard] Tables loaded before UserDefinedTypes – potential alias resolution issues.");
                if (!phaseFunctionsDone) consoleService.Warn("[ordering-guard] Tables loaded before Functions – mögliche fehlende Funktions-Metadaten für spätere Analysen.");
                phaseTablesDone = true;
                if (!phaseUserTypesDone) consoleService.Warn("[ordering-guard] Tables loaded before UserDefinedTypes – potential alias resolution issues.");

                // 3) Views je Build-Schema
                var views = new List<SnapshotView>();
                if (buildSchemas.Count > 0)
                {
                    var schemaLiteral = string.Join(',', buildSchemas.Select(s => $"'{s}'"));
                    const string viewSql = @"SELECT s.name AS schema_name, v.name AS view_name
                                             FROM sys.views AS v
                                             INNER JOIN sys.schemas AS s ON s.schema_id = v.schema_id
                                             WHERE s.name IN ({0})
                                             ORDER BY s.name, v.name"; // {0} wird unten ersetzt
                    var formattedViewSql = string.Format(viewSql, schemaLiteral);
                    var viewRows = await dbContext.ListAsync<ViewNameRow>(formattedViewSql, new List<Microsoft.Data.SqlClient.SqlParameter>(), System.Threading.CancellationToken.None);
                    foreach (var row in viewRows)
                    {
                        var cols = await dbContext.ViewColumnsListAsync(row.schema_name, row.view_name, System.Threading.CancellationToken.None);
                        views.Add(new SnapshotView
                        {
                            Schema = row.schema_name,
                            Name = row.view_name,
                            Columns = cols.Select(MapViewColumn).ToList()
                        });
                    }
                }
                // Views Phase abgeschlossen (Variable entfernt – Guard Meldungen behalten nur falls Reihenfolge verletzt würde)
                if (!phaseTablesDone) consoleService.Warn("[ordering-guard] Views loaded before Tables – unexpected sequence.");
                if (!phaseFunctionsDone) consoleService.Warn("[ordering-guard] Views loaded before Functions – unexpected sequence.");
                if (!phaseTablesDone) consoleService.Warn("[ordering-guard] Views loaded before Tables – unexpected sequence.");

                // Jetzt Functions sammeln (nach Tables + Views)
                try
                {
                    var fnCollector = new FunctionSnapshotCollector(dbContext, expandedSnapshotService, consoleService);
                    // Snapshot-Vorlage mit Tabellen für Enrichment inside collector
                    var fnSnap = new SchemaSnapshot { Tables = tables, Functions = new List<SnapshotFunction>() };
                    await fnCollector.CollectAsync(fnSnap);
                    collectedFunctions = fnSnap.Functions ?? new List<SnapshotFunction>();
                    phaseFunctionsDone = true;
                    consoleService.Verbose("[fn-summary] functions=" + collectedFunctions.Count);
                }
                catch (Exception fnEx)
                {
                    consoleService.Warn($"[fn-collect-error] {fnEx.Message}");
                    collectedFunctions = new List<SnapshotFunction>();
                }

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

                var fingerprint = schemaSnapshotService.BuildFingerprint(serverName, databaseName, buildSchemas, procedures.Count, udtts.Count, parserVersion: 8);
                var serverHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(serverName ?? "?"))).Substring(0, 16);

                var snapshot = new SchemaSnapshot
                {
                    Fingerprint = fingerprint,
                    Database = new SnapshotDatabase { ServerHash = serverHash, Name = databaseName },
                    Procedures = procedures,
                    Schemas = schemaSnapshots,
                    UserDefinedTableTypes = udtts,
                    Tables = tables,
                    Views = views,
                    UserDefinedTypes = userDefinedTypes,
                    Functions = (collectedFunctions ?? new List<SnapshotFunction>())
                        .OrderBy(f => f.Schema, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    FunctionsVersion = (collectedFunctions != null && collectedFunctions.Count > 0) ? 2 : null,
                    Parser = new SnapshotParserInfo { ToolVersion = config.TargetFramework, ResultSetParserVersion = 8 },
                    Stats = new SnapshotStats
                    {
                        ProcedureTotal = procedures.Count,
                        ProcedureLoaded = procedures.Count(p => p.ResultSets != null && p.ResultSets.Count > 0),
                        ProcedureSkipped = procedures.Count(p => p.ResultSets == null || p.ResultSets.Count == 0),
                        UdttTotal = udtts.Count,
                        TableTotal = tables.Count,
                        ViewTotal = views.Count,
                        UserDefinedTypeTotal = userDefinedTypes.Count
                    }
                };

                // Zentrale Post-Snapshot Enrichment Phase für Function JSON Columns (displayName, initials etc.)
                try
                {
                    var colEnricher = new ColumnEnrichmentService();
                    colEnricher.EnrichFunctions(snapshot, consoleService);
                }
                catch (Exception enrEx)
                {
                    consoleService.Warn($"[fn-enrich-post-error] {enrEx.Message}");
                }

                // Spätere Function-Sammlung entfällt – bereits in früher Phase erledigt (siehe fn-early-summary Log)
                // Hinweis: Frühe Function-Sammlung entfernt – zentrale Sammlung nach Tables/Views.

                // Monolithic legacy snapshot save removed (architecture refactor)
                // schemaSnapshotService.Save(snapshot); // disabled
                try
                {
                    // Use injected expanded snapshot service (no manual new())
                    expandedSnapshotService.SaveExpanded(snapshot);
                    consoleService.Verbose($"[snapshot-expanded] index + {procedures.Count} procedure file(s) + {udtts.Count} tabletype file(s) written");
                }
                catch (Exception fx)
                {
                    consoleService.Warn($"[snapshot-expanded-error] {fx.Message}");
                }
                consoleService.Verbose($"[snapshot] saved (expanded only) fingerprint={fingerprint} procs={procedures.Count} fns={(snapshot.Functions?.Count ?? 0)} udtts={udtts.Count} tables={tables.Count} views={views.Count} udts={userDefinedTypes.Count}");
                // Hash pro UDTT jetzt im Cache statt im Snapshot: separate Cache-Datei erzeugen
                try
                {
                    var working = Utils.DirectoryUtils.GetWorkingDirectory();
                    if (!string.IsNullOrWhiteSpace(working))
                    {
                        var cacheDir = System.IO.Path.Combine(working, ".spocr", "cache");
                        System.IO.Directory.CreateDirectory(cacheDir);
                        var cachePath = System.IO.Path.Combine(cacheDir, "tabletype-hashes.json");
                        var cacheDoc = new
                        {
                            version = 1,
                            items = udtts.Select(u => new { name = $"{u.Schema}.{u.Name}", hash = u.Hash }).OrderBy(x => x.name).ToList()
                        };
                        var jsonCache = System.Text.Json.JsonSerializer.Serialize(cacheDoc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        System.IO.File.WriteAllText(cachePath, jsonCache);
                        consoleService.Verbose($"[cache] tabletype-hashes written count={udtts.Count}");
                    }
                }
                catch (Exception cex)
                {
                    consoleService.Warn($"[cache-write-failed] {cex.Message}");
                }
                // Informational migration note (legacy schema node removed)
                consoleService.Verbose("[migration] Legacy 'schema' node removed; snapshot + IgnoredSchemas are now authoritative.");
                // Always show run-level JSON enrichment summary (even if zero) unless logging is Off
                if (jsonTypeLogLevel != JsonTypeLogLevel.Off)
                {
                    var summaryLine = $"[json-type-run-summary] procedures={procedures.Count(p => p.ResultSets.Any(rs => rs.Columns.Any()))} resolvedColumns={enrichmentStats.ResolvedColumns} new={enrichmentStats.NewConcrete} upgrades={enrichmentStats.Upgrades}";
                    if (options.Verbose) consoleService.Verbose(summaryLine); else consoleService.Output(summaryLine);
                    // Detail: Per-Prozedur Zusammenfassung nur bei Debug/Trace Level oder explizitem Flag SPOCR_JSON_PROC_SUMMARY=true
                    try
                    {
                        var lvl = Environment.GetEnvironmentVariable("SPOCR_LOG_LEVEL")?.Trim().ToLowerInvariant();
                        var flag = Environment.GetEnvironmentVariable("SPOCR_JSON_PROC_SUMMARY")?.Trim().ToLowerInvariant();
                        bool showDetails = (lvl is "debug" or "trace") || (flag is "1" or "true" or "yes");
                        if (showDetails)
                        {
                            foreach (var proc in procedures.Where(p => p.ResultSets != null && p.ResultSets.Any()))
                            {
                                int rsCount = proc.ResultSets.Count;
                                int jsonSets = proc.ResultSets.Count(r => r.ReturnsJson);
                                int totalCols = proc.ResultSets.Sum(r => r.Columns?.Count ?? 0);
                                // Aggregat-Erkennung in SnapshotResultColumn nicht vorhanden -> Platzhalter (0)
                                int aggCols = 0;
                                var schemaName = proc.Schema ?? "dbo";
                                // Wichtig: bisher Verbose() -> erforderte --verbose Flag und ignorierte SPOCR_LOG_LEVEL/SPOCR_JSON_PROC_SUMMARY.
                                // Auf Output() umgestellt, damit Detailausgabe allein über showDetails (debug/trace Level oder Flag) gesteuert wird.
                                consoleService.Output($"[json-type-proc-summary] {schemaName}.{proc.Name} sets={rsCount} jsonSets={jsonSets} cols={totalCols} aggCols={aggCols}");
                            }
                        }
                    }
                    catch { }
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
        // Detect dual/next mode early (do not impact legacy build logic until after legacy generation for dual)
        var genMode = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(genMode)) genMode = "dual"; // default bridge phase behavior

        if (!configFile.Exists())
        {
            var genModeMissing = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE")?.Trim().ToLowerInvariant();
            var envConn = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_DB");
            if (genModeMissing is "dual" or "next" && !string.IsNullOrWhiteSpace(envConn))
            {
                consoleService.Warn("[bridge] spocr.json missing – build proceeding using .env values.");
            }
            else
            {
                consoleService.Error("Configuration file not found");
                consoleService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
                return ExecuteResultEnum.Error;
            }
        }

        await RunAutoUpdateAsync(options);

        if (await RunConfigVersionCheckAsync(options) == ExecuteResultEnum.Aborted)
            return ExecuteResultEnum.Aborted;

        consoleService.PrintTitle($"Build DataContext from {Constants.ConfigurationFile}");

        // Configure AST verbose diagnostics (bind/derived logs) according to global --verbose flag
        try { SpocR.Models.StoredProcedureContentModel.SetAstVerbose(options.Verbose); } catch { }

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
            if (options.Verbose)
            {
                try
                {
                    var working = Utils.DirectoryUtils.GetWorkingDirectory();
                    var outputBase = Utils.DirectoryUtils.GetWorkingDirectory(project.Output?.DataContext?.Path ?? "(null)");
                    consoleService.Verbose($"[diag-build] workingDir={working} outputBase={outputBase} genMode={genMode}");
                }
                catch { }
            }
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


            // Invoke vNext pipeline in dual/next mode: real TableTypes generation (always-on)
            if (genMode == "dual" || genMode == "next")
            {
                try
                {
                    consoleService.PrintSubTitle($"vNext ({genMode}) – TableTypes");
                    var targetProjectRoot = Utils.DirectoryUtils.GetWorkingDirectory(); // Zielprojekt (enthält .spocr)
                    var cfg = EnvConfiguration.Load(projectRoot: targetProjectRoot);
                    var renderer = new SimpleTemplateEngine();
                    // Templates leben im Tool-Repository, nicht im Zielprojekt
                    var toolRoot = System.IO.Directory.GetCurrentDirectory();
                    var templatesDir = System.IO.Path.Combine(toolRoot, "src", "SpocRVNext", "Templates");
                    ITemplateLoader? loader = System.IO.Directory.Exists(templatesDir) ? new FileSystemTemplateLoader(templatesDir) : null;
                    var metadata = new TableTypeMetadataProvider(targetProjectRoot); // Metadaten aus Zielprojekt (.spocr)
                    var generator = new TableTypesGenerator(cfg, metadata, renderer, loader, targetProjectRoot);
                    var count = generator.Generate();
                    consoleService.Gray($"[vnext] TableTypes generated: {count} artifacts (includes interface)");
                }
                catch (Exception vx)
                {
                    consoleService.Warn($"[vnext-warning] TableTypes generation failed: {vx.Message}");
                }
            }

            // (Removed) manager-level .env prefill: now handled centrally inside EnvConfiguration for consistency.

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

            // Access still required until full removal in v5 – locally suppress obsolete warning
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

        // Migration / normalization for Role (deprecation path)
        try
        {
            // If Role missing => fill with default (Kind=Default)
            if (config.Project != null && config.Project.Role == null)
            {
                config.Project.Role = new RoleModel();
            }
            // Deprecation warning when a non-default value is set
            // Deprecation warning only if old value (Lib/Extension) is used
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

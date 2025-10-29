using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpocR.Models;
using SpocRVNext.Configuration; // for EnvConfiguration
using SpocR.SpocRVNext.Data.Models;

namespace SpocR.Services;

public interface ISchemaMetadataProvider
{
    IReadOnlyList<SchemaModel> GetSchemas();
}

/// <summary>
/// Snapshot-backed schema metadata provider (single authoritative source).
/// Loads the latest snapshot from .spocr/schema and maps it to runtime models.
/// Throws when no snapshot exists (caller should instruct user to run 'spocr pull').
/// </summary>
public class SnapshotSchemaMetadataProvider : ISchemaMetadataProvider
{
    private readonly ISchemaSnapshotService _snapshotService;
    private readonly IConsoleService _console;
    private IReadOnlyList<SchemaModel> _schemas; // cached after first load
    private DateTime _lastLoadUtc = DateTime.MinValue;
    private string _fingerprint; // last loaded fingerprint

    public SnapshotSchemaMetadataProvider(ISchemaSnapshotService snapshotService, IConsoleService console)
    {
        _snapshotService = snapshotService;
        _console = console;
    }

    public IReadOnlyList<SchemaModel> GetSchemas()
    {
        // Reload conditions:
        // 1) First access (_schemas == null)
        // 2) Caller flagged --no-cache (CommandOptions.NoCache true)
        // 3) index.json file changed since last load (timestamp newer)
        try
        {
            var workingReload = Utils.DirectoryUtils.GetWorkingDirectory() ?? string.Empty;
            var schemaDirProbe = Path.Combine(workingReload, ".spocr", "schema");
            var indexPathProbe = Path.Combine(schemaDirProbe, "index.json");
            var indexLastWrite = File.Exists(indexPathProbe) ? File.GetLastWriteTimeUtc(indexPathProbe) : DateTime.MinValue;
            bool noCacheFlag = SpocR.Utils.CacheControl.ForceReload;
            // Performance-Fix: --no-cache erzwingt nur den ersten Reload (wenn _schemas noch nicht geladen).
            // Danach wird ForceReload zurückgesetzt, damit nachfolgende Generator-Aufrufe nicht jede Abfrage erneut laden.
            var shouldReload = _schemas == null || (noCacheFlag && _schemas == null) || (indexLastWrite > _lastLoadUtc && indexLastWrite != DateTime.MinValue);
            if (!shouldReload)
                return _schemas;
            if (_schemas != null)
                _console.Verbose("[snapshot-provider] reload triggered (no-cache flag or index.json updated)");
            _schemas = null;
        }
        catch { /* ignore reload detection errors */ }

        var working = Utils.DirectoryUtils.GetWorkingDirectory();
        var schemaDir = Path.Combine(working, ".spocr", "schema");
        if (!Directory.Exists(schemaDir))
        {
            throw new InvalidOperationException("No snapshot directory found (.spocr/schema). Run 'spocr pull' first.");
        }
        // Expanded layout: index.json + subfolders. Prefer loading this format when present.
        SchemaSnapshot snapshot = null;
        var indexPath = Path.Combine(schemaDir, "index.json");
        if (File.Exists(indexPath))
        {
            try
            {
                var fl = new SchemaSnapshotFileLayoutService();
                snapshot = fl.LoadExpanded();
                if (snapshot == null)
                {
                    throw new InvalidOperationException("Expanded snapshot index.json exists but could not be loaded.");
                }
                _console.Verbose($"[snapshot-provider] expanded layout loaded fingerprint={snapshot.Fingerprint} procs={snapshot.Procedures.Count} udtts={snapshot.UserDefinedTableTypes.Count}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load expanded snapshot index.json: {ex.Message}");
            }
        }
        else
        {
            // Fallback: legacy monolithic snapshot files (Fingerprint.json)
            var legacyFiles = Directory.GetFiles(schemaDir, "*.json");
            if (legacyFiles.Length == 0)
                throw new InvalidOperationException("No snapshot file found (.spocr/schema). Run 'spocr pull' first.");
            var latestLegacy = legacyFiles.Select(f => new FileInfo(f)).OrderByDescending(fi => fi.LastWriteTimeUtc).First();
            var fp = Path.GetFileNameWithoutExtension(latestLegacy.FullName);
            try
            {
                snapshot = _snapshotService.Load(fp);
                if (snapshot == null)
                    throw new InvalidOperationException("Legacy snapshot deserialization returned null.");
                _console.Verbose($"[snapshot-provider] legacy layout loaded fingerprint={snapshot.Fingerprint} procs={snapshot.Procedures.Count} udtts={snapshot.UserDefinedTableTypes.Count}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load legacy snapshot '{fp}': {ex.Message}");
            }
        }

        // Fingerprint logging already emitted above depending on layout

        // Load BuildSchemas allow-list from .env and process dynamic ignore lists via environment variables.
    List<string> ignored = new();
    SchemaStatusEnum defaultStatus = SchemaStatusEnum.Build;
        List<string>? buildSchemas = null; // SPOCR_BUILD_SCHEMAS positive allow-list

        try
        {
            var workingDir = Utils.DirectoryUtils.GetWorkingDirectory();
            var envConfig = EnvConfiguration.Load(projectRoot: workingDir);
            if (envConfig.BuildSchemas != null && envConfig.BuildSchemas.Count > 0)
            {
                buildSchemas = envConfig.BuildSchemas.ToList();
                _console.Verbose($"[snapshot-provider] using BuildSchemas from .env: {string.Join(",", buildSchemas)}");
            }
        }
        catch (Exception envEx)
        {
            _console.Verbose($"[snapshot-provider] env configuration load failed: {envEx.Message}");
            var buildSchemasRaw = Environment.GetEnvironmentVariable("SPOCR_BUILD_SCHEMAS");
            if (!string.IsNullOrWhiteSpace(buildSchemasRaw))
            {
                buildSchemas = buildSchemasRaw.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                if (buildSchemas.Count > 0)
                {
                    _console.Verbose($"[snapshot-provider] using SPOCR_BUILD_SCHEMAS from environment: {string.Join(",", buildSchemas)}");
                }
            }
        }

        var ignoredSchemasRaw = Environment.GetEnvironmentVariable("SPOCR_IGNORED_SCHEMAS");
        if (!string.IsNullOrWhiteSpace(ignoredSchemasRaw))
        {
            ignored = ignoredSchemasRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ignored.Count > 0)
            {
                _console.Verbose($"[snapshot-provider] using IgnoredSchemas from environment: {string.Join(",", ignored)}");
            }
        }

        var defaultStatusRaw = Environment.GetEnvironmentVariable("SPOCR_DEFAULT_SCHEMA_STATUS");
        if (!string.IsNullOrWhiteSpace(defaultStatusRaw) && Enum.TryParse<SchemaStatusEnum>(defaultStatusRaw, true, out var parsedStatus) && parsedStatus != SchemaStatusEnum.Undefined)
        {
            defaultStatus = parsedStatus;
            _console.Verbose($"[snapshot-provider] default schema status overridden via SPOCR_DEFAULT_SCHEMA_STATUS={defaultStatus}.");
        }

        bool hasIgnored = ignored?.Any() == true;
        if (hasIgnored) _console.Verbose($"[snapshot-provider] deriving schema statuses via IgnoredSchemas ({ignored.Count})");

        var userTypeLookup = BuildUserTypeLookup(snapshot?.UserDefinedTypes);

        var schemas = snapshot.Schemas.Select(s =>
                {
                    // Derive status: check SPOCR_BUILD_SCHEMAS first (positive allow-list), then fall back to legacy ignore logic
                    SchemaStatusEnum status;

                    // If SPOCR_BUILD_SCHEMAS is specified, use it as positive allow-list
                    if (buildSchemas != null && buildSchemas.Any())
                    {
                        status = buildSchemas.Contains(s.Name, StringComparer.OrdinalIgnoreCase)
                            ? SchemaStatusEnum.Build
                            : SchemaStatusEnum.Ignore;
                    }
                    // Otherwise use legacy logic with IgnoredSchemas
                    else
                    {
                        status = ignored.Contains(s.Name, StringComparer.OrdinalIgnoreCase)
                            ? SchemaStatusEnum.Ignore
                            : defaultStatus;
                    }

                    // Filter procedures by SPOCR_BUILD_PROCEDURES if specified
                    var proceduresInSchema = snapshot.Procedures
                        .Where(p => p.Schema.Equals(s.Name, StringComparison.OrdinalIgnoreCase));

                    // Apply procedure-level filtering 
                    // DISABLED in Code Generation - procedure filtering is only for schema snapshots (pull command)
                    // Code generation is controlled exclusively by SPOCR_BUILD_SCHEMAS
                    /*
                    var buildProceduresRaw = Environment.GetEnvironmentVariable("SPOCR_BUILD_PROCEDURES");
                    if (!string.IsNullOrWhiteSpace(buildProceduresRaw))
                    {
                        var buildProcedures = new HashSet<string>(buildProceduresRaw
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(sp => sp.Trim())
                            .Where(sp => sp.Length > 0), StringComparer.OrdinalIgnoreCase);

                        if (buildProcedures.Count > 0)
                        {
                            proceduresInSchema = proceduresInSchema.Where(p =>
                            {
                                var fullName = $"{p.Schema}.{p.Name}";
                                var procedureName = p.Name;
                                return buildProcedures.Contains(fullName) || buildProcedures.Contains(procedureName);
                            });
                            _console.Verbose($"[snapshot-provider] applying SPOCR_BUILD_PROCEDURES filter for schema {s.Name}");
                        }
                    }
                    */

                    var spList = proceduresInSchema
                        .Select(p => new StoredProcedureModel(new SpocR.SpocRVNext.Data.Models.StoredProcedure
                        {
                            SchemaName = p.Schema,
                            Name = p.Name,
                            // ModifiedTicks nicht mehr Teil des Snapshots: Fallback auf GeneratedUtc / DateTime.MinValue
                            Modified = DateTime.MinValue
                        })
                        {
                            ModifiedTicks = null,
                            Input = p.Inputs.Select(i =>
                            {
                                var storedInput = new SpocR.SpocRVNext.Data.Models.StoredProcedureInput
                                {
                                    Name = i.Name,
                                    IsNullable = i.IsNullable ?? false,
                                    MaxLength = i.MaxLength ?? 0,
                                    IsOutput = i.IsOutput ?? false,
                                    HasDefaultValue = i.HasDefaultValue == true,
                                    Precision = i.Precision,
                                    Scale = i.Scale
                                };

                                bool isTableType = !string.IsNullOrWhiteSpace(i.TableTypeSchema) && !string.IsNullOrWhiteSpace(i.TableTypeName);
                                storedInput.IsTableType = isTableType;

                                if (isTableType)
                                {
                                    storedInput.UserTypeSchemaName = i.TableTypeSchema;
                                    storedInput.UserTypeName = i.TableTypeName;
                                    storedInput.SqlTypeName = BuildSqlTypeName(i.TableTypeSchema, i.TableTypeName) ?? i.TypeRef ?? string.Empty;
                                }
                                else
                                {
                                    var (schema, name) = SplitTypeRef(i.TypeRef);
                                    var resolvedUdt = ResolveUserDefinedType(schema, name, userTypeLookup);

                                    if (resolvedUdt != null)
                                    {
                                        storedInput.SqlTypeName = resolvedUdt.BaseSqlTypeName ?? name ?? i.TypeRef ?? string.Empty;
                                        storedInput.UserTypeSchemaName = resolvedUdt.Schema;
                                        storedInput.UserTypeName = resolvedUdt.Name;
                                        storedInput.BaseSqlTypeName = resolvedUdt.BaseSqlTypeName;
                                        if (resolvedUdt.MaxLength.HasValue)
                                        {
                                            storedInput.MaxLength = resolvedUdt.MaxLength.Value;
                                        }
                                        storedInput.Precision ??= resolvedUdt.Precision;
                                        storedInput.Scale ??= resolvedUdt.Scale;
                                        if (!i.IsNullable.HasValue && resolvedUdt.IsNullable.HasValue)
                                        {
                                            storedInput.IsNullable = resolvedUdt.IsNullable.Value;
                                        }
                                    }
                                    else
                                    {
                                        storedInput.SqlTypeName = ResolveSqlTypeNameOrFallback(schema, name, i.TypeRef);

                                        if (!IsSystemSchema(schema))
                                        {
                                            storedInput.UserTypeSchemaName = schema;
                                            storedInput.UserTypeName = name;
                                            storedInput.BaseSqlTypeName ??= storedInput.SqlTypeName;
                                        }
                                        else
                                        {
                                            storedInput.BaseSqlTypeName = name ?? storedInput.SqlTypeName;
                                        }
                                    }
                                }

                                return new StoredProcedureInputModel(storedInput);
                            }).ToList(),
                            Content = new StoredProcedureContentModel
                            {
                                Definition = null,
                                ContainsSelect = true,
                                ResultSets = PostProcessResultSets(p, userTypeLookup)
                            }
                        }).ToList();
                    var ttList = snapshot.UserDefinedTableTypes
                        .Where(u => u.Schema.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(u => new TableTypeModel(new SpocR.SpocRVNext.Data.Models.TableType
                        {
                            Name = u.Name,
                            SchemaName = u.Schema,
                            UserTypeId = u.UserTypeId,
                            Columns = u.Columns.Select(c => MapUdttColumnToModel(c, userTypeLookup)).ToList()
                        }, u.Columns.Select(c => MapUdttColumnToModel(c, userTypeLookup)).ToList())).ToList();
                    return new SchemaModel
                    {
                        Name = s.Name,
                        Status = status,
                        StoredProcedures = spList,
                        TableTypes = ttList
                    };
                }).ToList();

        _schemas = schemas;
        _lastLoadUtc = DateTime.UtcNow;
        _fingerprint = snapshot?.Fingerprint;
        try { _console.Verbose($"[snapshot-provider] loaded schemas={_schemas.Count} fingerprint={_fingerprint}"); } catch { }
        // Reset ForceReload after first actual reload so subsequent GetSchemas() calls are fast.
        if (SpocR.Utils.CacheControl.ForceReload)
        {
            SpocR.Utils.CacheControl.ForceReload = false;
        }
        // Diagnostics: identify JSON result sets without columns (RawJson fallback)
        try
        {
            foreach (var sc in _schemas)
            {
                foreach (var sp in sc.StoredProcedures ?? Enumerable.Empty<StoredProcedureModel>())
                {
                    var sets = sp.Content?.ResultSets;
                    if (sets == null) continue;
                    for (var i = 0; i < sets.Count; i++)
                    {
                        var rs = sets[i];
                        if (rs.ReturnsJson)
                        {
                            var colCount = rs.Columns?.Count ?? 0;
                            if (colCount == 0)
                            {
                                _console.Verbose($"[snapshot-provider-json-empty] {sp.SchemaName}.{sp.Name} set#{i + 1} JSON columns=0 (RawJson Fallback) root='{rs.JsonRootProperty ?? "<null>"}'");
                            }
                            else
                            {
                                _console.Verbose($"[snapshot-provider-json] {sp.SchemaName}.{sp.Name} set#{i + 1} JSON columns={colCount}");
                            }
                        }
                    }
                }
            }
        }
        catch { /* best effort diag */ }
        return _schemas;
    }

    private static Column MapUdttColumnToModel(SnapshotUdttColumn column, IDictionary<string, SnapshotUserDefinedType> userTypeLookup)
    {
        var mapped = new Column
        {
            Name = column.Name,
            IsNullable = column.IsNullable ?? false,
            MaxLength = column.MaxLength ?? 0,
            Precision = column.Precision,
            Scale = column.Scale
        };

        var (schema, name) = SplitTypeRef(column.TypeRef);
        var resolvedUdt = ResolveUserDefinedType(schema, name, userTypeLookup);

        if (resolvedUdt != null)
        {
            mapped.SqlTypeName = resolvedUdt.BaseSqlTypeName ?? name ?? column.TypeRef ?? string.Empty;
            mapped.UserTypeSchemaName = resolvedUdt.Schema;
            mapped.UserTypeName = resolvedUdt.Name;
            mapped.BaseSqlTypeName = resolvedUdt.BaseSqlTypeName;
            if (resolvedUdt.MaxLength.HasValue)
            {
                mapped.MaxLength = resolvedUdt.MaxLength.Value;
            }
            mapped.Precision ??= resolvedUdt.Precision;
            mapped.Scale ??= resolvedUdt.Scale;
            if (!column.IsNullable.HasValue && resolvedUdt.IsNullable.HasValue)
            {
                mapped.IsNullable = resolvedUdt.IsNullable.Value;
            }
        }
        else
        {
            mapped.SqlTypeName = ResolveSqlTypeNameOrFallback(schema, name, column.TypeRef);
            if (!IsSystemSchema(schema))
            {
                mapped.UserTypeSchemaName = schema;
                mapped.UserTypeName = name;
                mapped.BaseSqlTypeName ??= mapped.SqlTypeName;
            }
            else
            {
                mapped.BaseSqlTypeName = name ?? mapped.SqlTypeName;
            }
        }

        return mapped;
    }

    private static IReadOnlyList<StoredProcedureContentModel.ResultSet> PostProcessResultSets(SnapshotProcedure p, IDictionary<string, SnapshotUserDefinedType> userTypeLookup)
    {
        StoredProcedureContentModel.ResultColumn MapSnapshotCol(SnapshotResultColumn c)
        {
            var rc = new StoredProcedureContentModel.ResultColumn
            {
                Name = c.Name,
                SqlTypeName = null,
                IsNullable = c.IsNullable,
                MaxLength = c.MaxLength,
                IsNestedJson = c.IsNestedJson,
                ReturnsJson = c.ReturnsJson,
                ReturnsJsonArray = c.ReturnsJsonArray,
                JsonRootProperty = c.JsonRootProperty,
                DeferredJsonExpansion = c.DeferredJsonExpansion == true ? true : null,
                Reference = c.Reference != null ? new StoredProcedureContentModel.ColumnReferenceInfo
                {
                    Kind = c.Reference.Kind,
                    Schema = c.Reference.Schema,
                    Name = c.Reference.Name
                } : null
            };

            var (schema, name) = SplitTypeRef(c.TypeRef);
            var resolvedUdt = ResolveUserDefinedType(schema, name, userTypeLookup);
            if (resolvedUdt != null)
            {
                rc.SqlTypeName = resolvedUdt.BaseSqlTypeName ?? name ?? c.TypeRef ?? string.Empty;
                rc.UserTypeSchemaName = resolvedUdt.Schema;
                rc.UserTypeName = resolvedUdt.Name;
                rc.MaxLength ??= resolvedUdt.MaxLength;
                if (!c.IsNullable.HasValue && resolvedUdt.IsNullable.HasValue)
                {
                    rc.IsNullable = resolvedUdt.IsNullable;
                }
            }
            else
            {
                rc.SqlTypeName = ResolveSqlTypeNameOrFallback(schema, name, c.TypeRef);
                if (!IsSystemSchema(schema))
                {
                    rc.UserTypeSchemaName = schema;
                    rc.UserTypeName = name;
                }
            }

            var nestedSource = c.Columns;
            if (nestedSource != null && nestedSource.Count > 0)
            {
                rc.Columns = nestedSource.Select(MapSnapshotCol).ToArray();
            }
            else
            {
                rc.Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>();
            }
            return rc;
        }

        var sets = p.ResultSets.Select(rs => new StoredProcedureContentModel.ResultSet
        {
            ReturnsJson = rs.ReturnsJson,
            ReturnsJsonArray = rs.ReturnsJsonArray,
            JsonRootProperty = rs.JsonRootProperty,
            ExecSourceSchemaName = rs.ExecSourceSchemaName,
            ExecSourceProcedureName = rs.ExecSourceProcedureName,
            HasSelectStar = rs.HasSelectStar == true,
            Reference = rs.Reference != null ? new StoredProcedureContentModel.ColumnReferenceInfo
            {
                Kind = rs.Reference.Kind,
                Schema = rs.Reference.Schema,
                Name = rs.Reference.Name
            }
            : (!string.IsNullOrWhiteSpace(rs.ExecSourceProcedureName)
                ? new StoredProcedureContentModel.ColumnReferenceInfo
                {
                    Kind = "Procedure",
                    Schema = rs.ExecSourceSchemaName ?? "dbo",
                    Name = rs.ExecSourceProcedureName
                }
                : null),
            Columns = rs.Columns.Select(MapSnapshotCol).ToArray()
        }).ToList();

        return sets.ToArray();
    }

    private static Dictionary<string, SnapshotUserDefinedType> BuildUserTypeLookup(IEnumerable<SnapshotUserDefinedType> userDefinedTypes)
    {
        var lookup = new Dictionary<string, SnapshotUserDefinedType>(StringComparer.OrdinalIgnoreCase);
        if (userDefinedTypes == null)
        {
            return lookup;
        }

        foreach (var udt in userDefinedTypes)
        {
            if (udt == null) continue;
            if (string.IsNullOrWhiteSpace(udt.Schema) || string.IsNullOrWhiteSpace(udt.Name)) continue;

            var key = string.Concat(udt.Schema, ".", udt.Name);
            lookup[key] = udt;
            if (!lookup.ContainsKey(udt.Name))
            {
                lookup[udt.Name] = udt;
            }

            if (udt.Name.StartsWith("_", StringComparison.Ordinal))
            {
                var trimmed = udt.Name.TrimStart('_');
                var trimmedKey = string.Concat(udt.Schema, ".", trimmed);
                if (!lookup.ContainsKey(trimmedKey))
                {
                    lookup[trimmedKey] = udt;
                }
                if (!lookup.ContainsKey(trimmed))
                {
                    lookup[trimmed] = udt;
                }
            }
        }

        return lookup;
    }

    private static SnapshotUserDefinedType ResolveUserDefinedType(string? schema, string? name, IDictionary<string, SnapshotUserDefinedType> lookup)
    {
        if (lookup == null || lookup.Count == 0 || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        SnapshotUserDefinedType udt = null;
        if (!string.IsNullOrWhiteSpace(schema))
        {
            lookup.TryGetValue(string.Concat(schema, ".", name), out udt);
        }

        if (udt == null)
        {
            lookup.TryGetValue(name, out udt);
        }

        if (udt == null && name.StartsWith("_", StringComparison.Ordinal))
        {
            var trimmed = name.TrimStart('_');
            if (!string.IsNullOrWhiteSpace(schema))
            {
                lookup.TryGetValue(string.Concat(schema, ".", trimmed), out udt);
            }
            if (udt == null)
            {
                lookup.TryGetValue(trimmed, out udt);
            }
        }

        return udt;
    }

    private static string ResolveSqlTypeNameOrFallback(string? schema, string? name, string? typeRef)
    {
        var candidate = BuildSqlTypeName(schema, name) ?? typeRef ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return System.Data.SqlDbType.Variant.ToString();
        }

        var normalized = candidate.Split('(')[0];
        if (Enum.TryParse<System.Data.SqlDbType>(normalized, true, out _))
        {
            return candidate;
        }

        return System.Data.SqlDbType.NVarChar.ToString();
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

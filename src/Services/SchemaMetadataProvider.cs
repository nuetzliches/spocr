using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpocR.Managers;
using SpocR.Models;
using SpocRVNext.Configuration; // for EnvConfiguration

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
    private readonly FileManager<ConfigurationModel> _configFile; // for deriving ignored schemas dynamically
    private IReadOnlyList<SchemaModel> _schemas; // cached after first load
    private DateTime _lastLoadUtc = DateTime.MinValue;
    private string _fingerprint; // last loaded fingerprint

    public SnapshotSchemaMetadataProvider(ISchemaSnapshotService snapshotService, IConsoleService console, FileManager<ConfigurationModel> configFile = null)
    {
        _snapshotService = snapshotService;
        _console = console;
        _configFile = configFile; // optional (tests may omit)
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

        // Load current config (best-effort) to derive IgnoredSchemas dynamically; if unavailable fallback to snapshot status field.
        List<string> ignored = null; SchemaStatusEnum defaultStatus = SchemaStatusEnum.Build;
        List<string> buildSchemas = null; // SPOCR_BUILD_SCHEMAS positive allow-list
        try
        {
            var cfg = _configFile?.Config; // FileManager keeps last loaded config
            ignored = cfg?.Project?.IgnoredSchemas ?? new List<string>();
            defaultStatus = cfg?.Project?.DefaultSchemaStatus ?? SchemaStatusEnum.Build;

            // Load build schemas from .env file (same logic as vNext generators)
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
            catch
            {
                // Fallback to direct environment variable check (legacy behavior)
                var buildSchemasRaw = Environment.GetEnvironmentVariable("SPOCR_BUILD_SCHEMAS");
                if (!string.IsNullOrWhiteSpace(buildSchemasRaw))
                {
                    buildSchemas = buildSchemasRaw.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                    _console.Verbose($"[snapshot-provider] using SPOCR_BUILD_SCHEMAS from environment: {string.Join(",", buildSchemas)}");
                }
            }
        }
        catch { ignored = new List<string>(); }

        bool hasIgnored = ignored?.Any() == true;
        if (hasIgnored) _console.Verbose($"[snapshot-provider] deriving schema statuses via IgnoredSchemas ({ignored.Count})");

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
                else if (defaultStatus == SchemaStatusEnum.Ignore)
                {
                    status = ignored.Contains(s.Name, StringComparer.OrdinalIgnoreCase)
                        ? SchemaStatusEnum.Ignore
                        : SchemaStatusEnum.Build;
                }
                else
                {
                    status = ignored.Contains(s.Name, StringComparer.OrdinalIgnoreCase)
                        ? SchemaStatusEnum.Ignore
                        : defaultStatus;
                }
                var spList = snapshot.Procedures
                    .Where(p => p.Schema.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(p => new StoredProcedureModel(new DataContext.Models.StoredProcedure
                    {
                        SchemaName = p.Schema,
                        Name = p.Name,
                        // ModifiedTicks nicht mehr Teil des Snapshots: Fallback auf GeneratedUtc / DateTime.MinValue
                        Modified = DateTime.MinValue
                    })
                    {
                        ModifiedTicks = null,
                        Input = p.Inputs.Select(i => new StoredProcedureInputModel(new DataContext.Models.StoredProcedureInput
                        {
                            Name = i.Name,
                            SqlTypeName = i.SqlTypeName,
                            IsNullable = i.IsNullable ?? false,
                            MaxLength = i.MaxLength ?? 0,
                            IsOutput = i.IsOutput ?? false,
                            IsTableType = !string.IsNullOrWhiteSpace(i.TableTypeName) && !string.IsNullOrWhiteSpace(i.TableTypeSchema),
                            UserTypeName = i.TableTypeName,
                            UserTypeSchemaName = i.TableTypeSchema
                        })).ToList(),
                        Content = new StoredProcedureContentModel
                        {
                            Definition = null,
                            ContainsSelect = true,
                            ResultSets = PostProcessResultSets(p)
                        }
                    }).ToList();
                var ttList = snapshot.UserDefinedTableTypes
                    .Where(u => u.Schema.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(u => new TableTypeModel(new DataContext.Models.TableType
                    {
                        Name = u.Name,
                        SchemaName = u.Schema,
                        UserTypeId = u.UserTypeId,
                        Columns = u.Columns.Select(c => new DataContext.Models.Column
                        {
                            Name = c.Name,
                            SqlTypeName = c.SqlTypeName,
                            IsNullable = c.IsNullable ?? false,
                            MaxLength = c.MaxLength ?? 0,
                            UserTypeName = c.UserTypeName,
                            UserTypeSchemaName = c.UserTypeSchemaName,
                            BaseSqlTypeName = c.BaseSqlTypeName,
                            Precision = c.Precision,
                            Scale = c.Scale
                        }).ToList()
                    }, u.Columns.Select(c => new DataContext.Models.Column
                    {
                        Name = c.Name,
                        SqlTypeName = c.SqlTypeName,
                        IsNullable = c.IsNullable ?? false,
                        MaxLength = c.MaxLength ?? 0,
                        UserTypeName = c.UserTypeName,
                        UserTypeSchemaName = c.UserTypeSchemaName,
                        BaseSqlTypeName = c.BaseSqlTypeName,
                        Precision = c.Precision,
                        Scale = c.Scale
                    }).ToList())).ToList();
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

    private static IReadOnlyList<StoredProcedureContentModel.ResultSet> PostProcessResultSets(SnapshotProcedure p)
    {
        StoredProcedureContentModel.ResultColumn MapSnapshotCol(SnapshotResultColumn c)
        {
            // Prefer flattened v6 fields
            bool hasFlattened = c.IsNestedJson == true || c.ReturnsJson == true || (c.Columns != null && c.Columns.Count > 0);
#pragma warning disable CS0612
            var rc = new StoredProcedureContentModel.ResultColumn
            {
                Name = c.Name,
                SqlTypeName = c.SqlTypeName,
                IsNullable = c.IsNullable,
                MaxLength = c.MaxLength,
                UserTypeSchemaName = c.UserTypeSchemaName,
                UserTypeName = c.UserTypeName,
                IsNestedJson = hasFlattened ? c.IsNestedJson : (c.JsonResult != null ? true : null),
                ReturnsJson = hasFlattened ? c.ReturnsJson : c.JsonResult?.ReturnsJson,
                ReturnsJsonArray = hasFlattened ? c.ReturnsJsonArray : c.JsonResult?.ReturnsJsonArray,
                JsonRootProperty = hasFlattened ? c.JsonRootProperty : c.JsonResult?.JsonRootProperty,
                DeferredJsonExpansion = c.DeferredJsonExpansion == true ? true : null,
                Reference = c.Reference != null ? new StoredProcedureContentModel.ColumnReferenceInfo
                {
                    Kind = c.Reference.Kind,
                    Schema = c.Reference.Schema,
                    Name = c.Reference.Name
                } : null
            };
            var nestedSource = hasFlattened ? c.Columns : c.JsonResult?.Columns;
#pragma warning restore CS0612
            if (nestedSource != null && nestedSource.Count > 0)
            {
                rc.Columns = nestedSource.Select(MapSnapshotCol).ToArray();
            }
            else rc.Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>();
            return rc;
        }
        var sets = p.ResultSets.Select(rs => new StoredProcedureContentModel.ResultSet
        {
            ReturnsJson = rs.ReturnsJson,
            ReturnsJsonArray = rs.ReturnsJsonArray,
            // removed flag
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

        // Placeholder sets (forwarding references) are never filtered now because they are required to resolve the target model.

        // Entfernt: automatische legacy Einzelspalten-Konvertierung (JSON_F52E2B61...)
        // Optional reaktivierbar via Flag SPOCR_JSON_LEGACY_SINGLE=1 bereits im SchemaManager für Live-Erkennung.
        return sets.ToArray();
    }
}

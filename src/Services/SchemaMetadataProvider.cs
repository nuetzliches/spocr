using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpocR.Managers;
using SpocR.Models;

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

    public SnapshotSchemaMetadataProvider(ISchemaSnapshotService snapshotService, IConsoleService console, FileManager<ConfigurationModel> configFile = null)
    {
        _snapshotService = snapshotService;
        _console = console;
        _configFile = configFile; // optional (tests may omit)
    }

    public IReadOnlyList<SchemaModel> GetSchemas()
    {
        if (_schemas != null) return _schemas;

        var working = Utils.DirectoryUtils.GetWorkingDirectory();
        var schemaDir = Path.Combine(working, ".spocr", "schema");
        if (!Directory.Exists(schemaDir))
        {
            throw new InvalidOperationException("No snapshot directory found (.spocr/schema). Run 'spocr pull' first.");
        }
        var files = Directory.GetFiles(schemaDir, "*.json");
        if (files.Length == 0)
        {
            throw new InvalidOperationException("No snapshot file found (.spocr/schema/*.json). Run 'spocr pull' first.");
        }
        // Pick latest by last write time
        var latest = files.Select(f => new FileInfo(f)).OrderByDescending(fi => fi.LastWriteTimeUtc).First();
        var fp = Path.GetFileNameWithoutExtension(latest.FullName);
        SchemaSnapshot snapshot;
        try
        {
            snapshot = _snapshotService.Load(fp);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load snapshot '{fp}': {ex.Message}");
        }

        _console.Verbose($"[snapshot-provider] using fingerprint={snapshot.Fingerprint} procs={snapshot.Procedures.Count} udtts={snapshot.UserDefinedTableTypes.Count}");

        // Load current config (best-effort) to derive IgnoredSchemas dynamically; if unavailable fallback to snapshot status field.
        List<string> ignored = null; SchemaStatusEnum defaultStatus = SchemaStatusEnum.Build;
        try
        {
            var cfg = _configFile?.Config; // FileManager keeps last loaded config
            ignored = cfg?.Project?.IgnoredSchemas ?? new List<string>();
            defaultStatus = cfg?.Project?.DefaultSchemaStatus ?? SchemaStatusEnum.Build;
        }
        catch { ignored = new List<string>(); }

        bool hasIgnored = ignored?.Any() == true;
        if (hasIgnored) _console.Verbose($"[snapshot-provider] deriving schema statuses via IgnoredSchemas ({ignored.Count})");

        var schemas = snapshot.Schemas.Select(s =>
            {
                // Derive status: if default=Ignore we promote all non-explicit schemas to Build (first-run + subsequent semantics)
                SchemaStatusEnum status;
                if (defaultStatus == SchemaStatusEnum.Ignore)
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
                        Modified = new DateTime(p.ModifiedTicks, DateTimeKind.Utc)
                    })
                    {
                        ModifiedTicks = p.ModifiedTicks,
                        Input = p.Inputs.Select(i => new StoredProcedureInputModel(new DataContext.Models.StoredProcedureInput
                        {
                            Name = i.Name,
                            SqlTypeName = i.SqlTypeName,
                            IsNullable = i.IsNullable,
                            MaxLength = i.MaxLength,
                            IsOutput = i.IsOutput,
                            IsTableType = i.IsTableType,
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
                            IsNullable = c.IsNullable,
                            MaxLength = c.MaxLength
                        }).ToList()
                    }, u.Columns.Select(c => new DataContext.Models.Column
                    {
                        Name = c.Name,
                        SqlTypeName = c.SqlTypeName,
                        IsNullable = c.IsNullable,
                        MaxLength = c.MaxLength
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
        return _schemas;
    }

    private static IReadOnlyList<StoredProcedureContentModel.ResultSet> PostProcessResultSets(SnapshotProcedure p)
    {
        var sets = p.ResultSets.Select(rs => new StoredProcedureContentModel.ResultSet
        {
            ReturnsJson = rs.ReturnsJson,
            ReturnsJsonArray = rs.ReturnsJsonArray,
            ReturnsJsonWithoutArrayWrapper = rs.ReturnsJsonWithoutArrayWrapper,
            JsonRootProperty = rs.JsonRootProperty,
            ExecSourceSchemaName = rs.ExecSourceSchemaName,
            ExecSourceProcedureName = rs.ExecSourceProcedureName,
            HasSelectStar = rs.HasSelectStar,
            Columns = rs.Columns.Select(c => new StoredProcedureContentModel.ResultColumn
            {
                JsonPath = c.JsonPath,
                Name = c.Name,
                SqlTypeName = c.SqlTypeName,
                IsNullable = c.IsNullable,
                MaxLength = c.MaxLength,
                UserTypeSchemaName = c.UserTypeSchemaName,
                UserTypeName = c.UserTypeName,
                JsonResult = c.JsonResult == null ? null : new StoredProcedureContentModel.JsonResultModel
                {
                    ReturnsJson = c.JsonResult.ReturnsJson,
                    ReturnsJsonArray = c.JsonResult.ReturnsJsonArray,
                    ReturnsJsonWithoutArrayWrapper = c.JsonResult.ReturnsJsonWithoutArrayWrapper,
                    JsonRootProperty = c.JsonResult.JsonRootProperty,
                    Columns = c.JsonResult.Columns?.Select(n => new StoredProcedureContentModel.ResultColumn
                    {
                        JsonPath = n.JsonPath,
                        Name = n.Name,
                        SqlTypeName = n.SqlTypeName,
                        IsNullable = n.IsNullable,
                        MaxLength = n.MaxLength,
                        UserTypeSchemaName = n.UserTypeSchemaName,
                        UserTypeName = n.UserTypeName
                    }).ToArray() ?? Array.Empty<StoredProcedureContentModel.ResultColumn>()
                }
            }).ToArray()
        }).ToList();

        // Heuristik: Einzelnes Set, Einzelne Legacy JSON Column -> Markiere als JSON
        if (sets.Count == 1)
        {
            var s = sets[0];
            if (!s.ReturnsJson && (s.Columns?.Count == 1))
            {
                var col = s.Columns[0];
                if (col.Name != null && col.Name.Equals("JSON_F52E2B61-18A1-11d1-B105-00805F49916B", StringComparison.OrdinalIgnoreCase)
                    && (col.SqlTypeName?.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    sets[0] = new StoredProcedureContentModel.ResultSet
                    {
                        ReturnsJson = true,
                        ReturnsJsonArray = true, // konservativ: FOR JSON PATH ohne WITHOUT_ARRAY_WRAPPER -> Array
                        ReturnsJsonWithoutArrayWrapper = false,
                        JsonRootProperty = null,
                        Columns = Array.Empty<StoredProcedureContentModel.ResultColumn>() // Struktur unbekannt
                    };
                }
            }
        }
        return sets.ToArray();
    }
}

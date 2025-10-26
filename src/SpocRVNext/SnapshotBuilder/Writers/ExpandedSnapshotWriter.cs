using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext;
using SpocR.DataContext.Models;
using SpocR.DataContext.Queries;
using SpocR.Models;
using SpocR.Services;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

/// <summary>
/// Writes procedure snapshot metadata into .spocr/schema/procedures using streaming JSON with deterministic ordering.
/// </summary>
internal sealed class ExpandedSnapshotWriter : ISnapshotWriter
{
    private readonly IConsoleService _console;
    private readonly DbContext _dbContext;
    private readonly ISchemaSnapshotService? _legacySnapshotService;
    private static readonly JsonSerializerOptions IndexSerializerOptions = new()
    {
        WriteIndented = true
    };

    public ExpandedSnapshotWriter(IConsoleService console, DbContext dbContext, ISchemaSnapshotService? legacySnapshotService)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _legacySnapshotService = legacySnapshotService;
    }

    public async Task<SnapshotWriteResult> WriteAsync(IReadOnlyList<ProcedureAnalysisResult> analyzedProcedures, SnapshotBuildOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= SnapshotBuildOptions.Default;
        if (analyzedProcedures == null || analyzedProcedures.Count == 0)
        {
            return new SnapshotWriteResult
            {
                FilesWritten = 0,
                FilesUnchanged = 0,
                UpdatedProcedures = Array.Empty<ProcedureAnalysisResult>()
            };
        }

        var projectRoot = ProjectRootResolver.ResolveCurrent();
        var schemaRoot = Path.Combine(projectRoot, ".spocr", "schema");
        var proceduresRoot = Path.Combine(schemaRoot, "procedures");
        Directory.CreateDirectory(proceduresRoot);

        var updated = new List<ProcedureAnalysisResult>(analyzedProcedures.Count);
        var filesWritten = 0;
        var filesUnchanged = 0;
        var verbose = options?.Verbose ?? false;

        var degreeOfParallelism = options?.MaxDegreeOfParallelism > 0
            ? options.MaxDegreeOfParallelism
            : Environment.ProcessorCount;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, degreeOfParallelism)
        };

        var writeResults = new ConcurrentBag<ProcedureWriteRecord>();

        await Parallel.ForEachAsync(
            analyzedProcedures.Select((item, index) => (item, index)),
            parallelOptions,
            async (entry, ct) =>
            {
                var (item, index) = entry;
                if (item == null)
                {
                    return;
                }

                ct.ThrowIfCancellationRequested();

                var descriptor = item.Descriptor ?? new ProcedureDescriptor();
                var fileName = string.IsNullOrWhiteSpace(item.SnapshotFile)
                    ? BuildDefaultSnapshotFile(descriptor)
                    : item.SnapshotFile;
                var filePath = Path.Combine(proceduresRoot, fileName);

                var jsonBytes = BuildProcedureJson(descriptor, item.Parameters, item.Ast);
                var writeOutcome = await WriteArtifactAsync(filePath, jsonBytes, ct).ConfigureAwait(false);
                if (writeOutcome.Wrote && verbose)
                {
                    _console.Verbose($"[snapshot-write] wrote {fileName}");
                }

                var result = new ProcedureAnalysisResult
                {
                    Descriptor = descriptor,
                    Ast = item.Ast,
                    WasReusedFromCache = item.WasReusedFromCache,
                    SourceLastModifiedUtc = item.SourceLastModifiedUtc,
                    SnapshotFile = fileName,
                    SnapshotHash = writeOutcome.Hash,
                    Parameters = item.Parameters,
                    Dependencies = item.Dependencies
                };

                writeResults.Add(new ProcedureWriteRecord(index, result, writeOutcome.Wrote));
            }).ConfigureAwait(false);

        var orderedWrites = writeResults
            .OrderBy(static record => record.Index)
            .ToList();

        filesWritten = orderedWrites.Count(static record => record.Wrote);
        filesUnchanged = orderedWrites.Count - filesWritten;
        updated.AddRange(orderedWrites.Select(static record => record.Result));

        var schemaArtifacts = await WriteSchemaArtifactsAsync(schemaRoot, options!, updated, cancellationToken).ConfigureAwait(false);
        filesWritten += schemaArtifacts.FilesWritten;
        filesUnchanged += schemaArtifacts.FilesUnchanged;

        var indexDocument = await UpdateIndexAsync(schemaRoot, updated, schemaArtifacts, cancellationToken).ConfigureAwait(false);

        await WriteLegacySnapshotAsync(indexDocument, updated, cancellationToken).ConfigureAwait(false);

        return new SnapshotWriteResult
        {
            FilesWritten = filesWritten,
            FilesUnchanged = filesUnchanged,
            UpdatedProcedures = updated
        };
    }

    private static byte[] BuildProcedureJson(ProcedureDescriptor descriptor, IReadOnlyList<StoredProcedureInput> parameters, StoredProcedureContentModel? ast)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", descriptor?.Schema ?? string.Empty);
            writer.WriteString("Name", descriptor?.Name ?? string.Empty);

            WriteParameters(writer, parameters);
            WriteResultSets(writer, ast?.ResultSets);

            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteParameters(Utf8JsonWriter writer, IReadOnlyList<StoredProcedureInput> parameters)
    {
        writer.WritePropertyName("Parameters");
        writer.WriteStartArray();
        if (parameters != null)
        {
            foreach (var input in parameters)
            {
                if (input == null) continue;

                writer.WriteStartObject();
                var name = NormalizeParameterName(input.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    writer.WriteString("Name", name);
                }

                var typeRef = BuildTypeRef(input);

                if (!string.IsNullOrWhiteSpace(typeRef))
                {
                    writer.WriteString("TypeRef", typeRef);
                }

                if (input.IsTableType)
                {
                    writer.WriteBoolean("IsTableType", true);
                }
                else
                {
                    if (input.IsNullable)
                    {
                        writer.WriteBoolean("IsNullable", true);
                    }
                    if (input.MaxLength > 0)
                    {
                        writer.WriteNumber("MaxLength", input.MaxLength);
                    }
                    if (input.Precision.HasValue && input.Precision.Value > 0)
                    {
                        writer.WriteNumber("Precision", input.Precision.Value);
                    }
                    if (input.Scale.HasValue && input.Scale.Value > 0)
                    {
                        writer.WriteNumber("Scale", input.Scale.Value);
                    }
                }

                if (input.IsOutput)
                {
                    writer.WriteBoolean("IsOutput", true);
                }
                if (input.HasDefaultValue)
                {
                    writer.WriteBoolean("HasDefaultValue", true);
                }

                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    private static void WriteResultSets(Utf8JsonWriter writer, IReadOnlyList<StoredProcedureContentModel.ResultSet>? resultSets)
    {
        writer.WritePropertyName("ResultSets");
        writer.WriteStartArray();
        if (resultSets != null)
        {
            foreach (var set in resultSets)
            {
                if (set == null || !ShouldIncludeResultSet(set)) continue;

                writer.WriteStartObject();
                if (set.ReturnsJson || set.ReturnsJsonArray)
                {
                    writer.WriteBoolean("ReturnsJson", set.ReturnsJson);
                    writer.WriteBoolean("ReturnsJsonArray", set.ReturnsJsonArray);
                    if (!string.IsNullOrWhiteSpace(set.JsonRootProperty))
                    {
                        writer.WriteString("JsonRootProperty", set.JsonRootProperty);
                    }
                }
                if (!string.IsNullOrWhiteSpace(set.ExecSourceSchemaName))
                {
                    writer.WriteString("ExecSourceSchemaName", set.ExecSourceSchemaName);
                }
                if (!string.IsNullOrWhiteSpace(set.ExecSourceProcedureName))
                {
                    writer.WriteString("ExecSourceProcedureName", set.ExecSourceProcedureName);
                }
                if (set.HasSelectStar)
                {
                    writer.WriteBoolean("HasSelectStar", true);
                }

                writer.WritePropertyName("Columns");
                writer.WriteStartArray();
                if (set.Columns != null)
                {
                    foreach (var column in set.Columns)
                    {
                        if (column == null) continue;
                        WriteResultColumn(writer, column);
                    }
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    private async Task<SchemaArtifactSummary> WriteSchemaArtifactsAsync(string schemaRoot, SnapshotBuildOptions options, IReadOnlyList<ProcedureAnalysisResult> updatedProcedures, CancellationToken cancellationToken)
    {
        var summary = new SchemaArtifactSummary();
        var schemaSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options?.Schemas != null)
        {
            foreach (var schema in options.Schemas)
            {
                if (!string.IsNullOrWhiteSpace(schema))
                {
                    schemaSet.Add(schema);
                }
            }
        }

        if (updatedProcedures != null)
        {
            foreach (var proc in updatedProcedures)
            {
                var schema = proc?.Descriptor?.Schema;
                if (!string.IsNullOrWhiteSpace(schema))
                {
                    schemaSet.Add(schema);
                }
            }
        }

        if (schemaSet.Count == 0)
        {
            return summary;
        }

        var escapedSchemas = schemaSet
            .Select(s => $"'{s.Replace("'", "''")}'")
            .ToArray();
        var schemaListString = string.Join(',', escapedSchemas);

        List<TableType> tableTypes = new();
        try
        {
            var list = await _dbContext.TableTypeListAsync(schemaListString, cancellationToken).ConfigureAwait(false);
            if (list != null)
            {
                tableTypes = list;
            }
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-tabletype] failed to enumerate table types: {ex.Message}");
        }

        var tableTypeRoot = Path.Combine(schemaRoot, "tabletypes");
        Directory.CreateDirectory(tableTypeRoot);
        var validTableTypeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tableType in tableTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tableType == null || string.IsNullOrWhiteSpace(tableType.SchemaName) || string.IsNullOrWhiteSpace(tableType.Name))
            {
                continue;
            }

            List<Column> columns = new();
            if (tableType.UserTypeId.HasValue)
            {
                try
                {
                    var columnList = await _dbContext.TableTypeColumnListAsync(tableType.UserTypeId.Value, cancellationToken).ConfigureAwait(false);
                    if (columnList != null)
                    {
                        columns = columnList;
                    }
                }
                catch (Exception ex)
                {
                    _console.Verbose($"[snapshot-tabletype] failed to load columns for {tableType.SchemaName}.{tableType.Name}: {ex.Message}");
                }
            }

            var jsonBytes = BuildTableTypeJson(tableType, columns);
            var fileName = BuildArtifactFileName(tableType.SchemaName, tableType.Name);
            var filePath = Path.Combine(tableTypeRoot, fileName);
            var outcome = await WriteArtifactAsync(filePath, jsonBytes, cancellationToken).ConfigureAwait(false);
            if (outcome.Wrote)
            {
                summary.FilesWritten++;
            }
            else
            {
                summary.FilesUnchanged++;
            }

            validTableTypeFiles.Add(fileName);
            summary.TableTypes.Add(new IndexTableTypeEntry
            {
                Schema = tableType.SchemaName,
                Name = tableType.Name,
                File = fileName,
                Hash = outcome.Hash
            });
        }

        PruneExtraneousFiles(tableTypeRoot, validTableTypeFiles);

        List<UserDefinedTypeRow> scalarTypes = new();
        try
        {
            var list = await _dbContext.UserDefinedScalarTypesAsync(cancellationToken).ConfigureAwait(false);
            if (list != null)
            {
                scalarTypes = list
                    .Where(t => t != null && !string.IsNullOrWhiteSpace(t.schema_name) && schemaSet.Contains(t.schema_name))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-udt] failed to enumerate user-defined types: {ex.Message}");
        }

        var scalarRoot = Path.Combine(schemaRoot, "types");
        Directory.CreateDirectory(scalarRoot);
        var validScalarFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in scalarTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type == null || string.IsNullOrWhiteSpace(type.schema_name) || string.IsNullOrWhiteSpace(type.user_type_name))
            {
                continue;
            }

            var jsonBytes = BuildScalarTypeJson(type);
            var fileName = BuildArtifactFileName(type.schema_name, type.user_type_name);
            var filePath = Path.Combine(scalarRoot, fileName);
            var outcome = await WriteArtifactAsync(filePath, jsonBytes, cancellationToken).ConfigureAwait(false);
            if (outcome.Wrote)
            {
                summary.FilesWritten++;
            }
            else
            {
                summary.FilesUnchanged++;
            }

            validScalarFiles.Add(fileName);
            summary.UserDefinedTypes.Add(new IndexUserDefinedTypeEntry
            {
                Schema = type.schema_name,
                Name = type.user_type_name,
                File = fileName,
                Hash = outcome.Hash
            });
        }

        PruneExtraneousFiles(scalarRoot, validScalarFiles);

        summary.TableTypes.Sort((a, b) =>
        {
            var schemaCompare = string.Compare(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase);
            return schemaCompare != 0 ? schemaCompare : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        summary.UserDefinedTypes.Sort((a, b) =>
        {
            var schemaCompare = string.Compare(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase);
            return schemaCompare != 0 ? schemaCompare : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return summary;
    }

    private static void WriteResultColumn(Utf8JsonWriter writer, StoredProcedureContentModel.ResultColumn column)
    {
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(column.Name))
        {
            writer.WriteString("Name", column.Name);
        }
        var typeRef = BuildTypeRef(column);
        if (!string.IsNullOrWhiteSpace(typeRef))
        {
            writer.WriteString("TypeRef", typeRef);
        }
        if (column.IsNullable == true)
        {
            writer.WriteBoolean("IsNullable", true);
        }
        if (column.MaxLength.HasValue && column.MaxLength.Value > 0)
        {
            writer.WriteNumber("MaxLength", column.MaxLength.Value);
        }
        if (column.ReturnsJson == true || column.IsNestedJson == true)
        {
            writer.WriteBoolean("ReturnsJson", true);
            if (column.ReturnsJsonArray.HasValue)
            {
                writer.WriteBoolean("ReturnsJsonArray", column.ReturnsJsonArray.Value);
            }
            if (!string.IsNullOrWhiteSpace(column.JsonRootProperty))
            {
                writer.WriteString("JsonRootProperty", column.JsonRootProperty);
            }
        }

        if (column.Columns != null && column.Columns.Count > 0)
        {
            writer.WritePropertyName("Columns");
            writer.WriteStartArray();
            foreach (var child in column.Columns)
            {
                if (child == null) continue;
                WriteResultColumn(writer, child);
            }
            writer.WriteEndArray();
        }

        if (column.Reference != null && (!string.IsNullOrWhiteSpace(column.Reference.Kind) || !string.IsNullOrWhiteSpace(column.Reference.Schema) || !string.IsNullOrWhiteSpace(column.Reference.Name)))
        {
            writer.WritePropertyName("Reference");
            writer.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(column.Reference.Kind))
            {
                writer.WriteString("Kind", column.Reference.Kind);
            }
            if (!string.IsNullOrWhiteSpace(column.Reference.Schema))
            {
                writer.WriteString("Schema", column.Reference.Schema);
            }
            if (!string.IsNullOrWhiteSpace(column.Reference.Name))
            {
                writer.WriteString("Name", column.Reference.Name);
            }
            writer.WriteEndObject();
        }
        if (column.DeferredJsonExpansion == true)
        {
            writer.WriteBoolean("DeferredJsonExpansion", true);
        }

        writer.WriteEndObject();
    }

    private static bool ShouldIncludeResultSet(StoredProcedureContentModel.ResultSet set)
    {
        if (set == null) return false;
        if (set.ReturnsJson || set.ReturnsJsonArray) return true;
        if (set.Columns != null && set.Columns.Count > 0) return true;
        if (!string.IsNullOrWhiteSpace(set.ExecSourceProcedureName)) return true;
        return false;
    }

    private static string NormalizeParameterName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw.TrimStart('@');
    }

    private static string? BuildTypeRef(StoredProcedureInput input)
    {
        if (input == null) return null;

        if (input.IsTableType && !string.IsNullOrWhiteSpace(input.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(input.UserTypeName))
        {
            return BuildTypeRef(input.UserTypeSchemaName, input.UserTypeName);
        }

        if (!string.IsNullOrWhiteSpace(input.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(input.UserTypeName))
        {
            return BuildTypeRef(input.UserTypeSchemaName, input.UserTypeName);
        }

        if (!string.IsNullOrWhiteSpace(input.SqlTypeName))
        {
            var normalized = NormalizeSqlTypeName(input.SqlTypeName);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return BuildTypeRef("sys", normalized);
            }
        }

        return null;
    }

    private static string? BuildTypeRef(Column column)
    {
        if (column == null) return null;
        if (!string.IsNullOrWhiteSpace(column.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(column.UserTypeName))
        {
            return BuildTypeRef(column.UserTypeSchemaName, column.UserTypeName);
        }

        if (!string.IsNullOrWhiteSpace(column.SqlTypeName))
        {
            var normalized = NormalizeSqlTypeName(column.SqlTypeName);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return BuildTypeRef("sys", normalized);
            }
        }

        return null;
    }

    private static string? BuildTypeRef(StoredProcedureContentModel.ResultColumn column)
    {
        if (column == null) return null;
        if (!string.IsNullOrWhiteSpace(column.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(column.UserTypeName))
        {
            return BuildTypeRef(column.UserTypeSchemaName, column.UserTypeName);
        }

        var sqlType = column.SqlTypeName;
        if (string.IsNullOrWhiteSpace(sqlType) && !string.IsNullOrWhiteSpace(column.CastTargetType))
        {
            sqlType = column.CastTargetType;
        }

        if (!string.IsNullOrWhiteSpace(sqlType))
        {
            var normalized = NormalizeSqlTypeName(sqlType);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return BuildTypeRef("sys", normalized);
            }
        }

        return null;
    }

    private static string? BuildTypeRef(string? schema, string? name)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name)) return null;
        return string.Concat(schema.Trim(), ".", name.Trim());
    }

    private static (string? Schema, string? Name) SplitTypeRef(string? typeRef)
    {
        if (string.IsNullOrWhiteSpace(typeRef)) return (null, null);
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

    private static string? NormalizeSqlTypeName(string? sqlTypeName)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeName)) return null;
        return sqlTypeName.Trim().ToLowerInvariant();
    }

    private static bool HasUserDefinedType(Column column)
    {
        if (column == null) return false;
        return !string.IsNullOrWhiteSpace(column.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(column.UserTypeName);
    }

    private static string BuildDefaultSnapshotFile(ProcedureDescriptor descriptor)
    {
        var schema = string.IsNullOrWhiteSpace(descriptor?.Schema) ? "unknown" : descriptor.Schema;
        var name = string.IsNullOrWhiteSpace(descriptor?.Name) ? "unnamed" : descriptor.Name;
        return $"{schema}.{name}.json";
    }

    private static string ComputeHash(string content)
    {
        return ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty));
    }

    private static string ComputeHash(byte[] content)
    {
        return ComputeHash(content.AsSpan());
    }

    private static string ComputeHash(ReadOnlySpan<byte> content)
    {
        var hashBytes = SHA256.HashData(content);
        return Convert.ToHexString(hashBytes).Substring(0, 16);
    }

    private static async Task<IndexDocument> UpdateIndexAsync(
        string schemaRoot,
        IReadOnlyList<ProcedureAnalysisResult> updated,
        SchemaArtifactSummary schemaArtifacts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(schemaRoot);
        var indexPath = Path.Combine(schemaRoot, "index.json");

        IndexDocument? existing = null;
        if (File.Exists(indexPath))
        {
            try
            {
                await using var stream = File.OpenRead(indexPath);
                existing = await JsonSerializer.DeserializeAsync<IndexDocument>(stream, IndexSerializerOptions, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                existing = null;
            }
        }

        var entries = new Dictionary<string, IndexProcedureEntry>(StringComparer.OrdinalIgnoreCase);
        if (existing?.Procedures != null)
        {
            foreach (var entry in existing.Procedures)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Schema) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }
                entries[BuildKey(entry.Schema, entry.Name)] = entry;
            }
        }

        foreach (var proc in updated ?? Array.Empty<ProcedureAnalysisResult>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = proc?.Descriptor;
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Schema) || string.IsNullOrWhiteSpace(descriptor.Name))
            {
                continue;
            }

            var key = BuildKey(descriptor.Schema, descriptor.Name);
            entries[key] = new IndexProcedureEntry
            {
                Schema = descriptor.Schema,
                Name = descriptor.Name,
                File = proc?.SnapshotFile ?? string.Empty,
                Hash = proc?.SnapshotHash ?? string.Empty
            };
        }

        var tableTypeEntries = new Dictionary<string, IndexTableTypeEntry>(StringComparer.OrdinalIgnoreCase);
        if ((schemaArtifacts?.TableTypes == null || schemaArtifacts.TableTypes.Count == 0) && existing?.TableTypes != null)
        {
            foreach (var entry in existing.TableTypes)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Schema) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }
                tableTypeEntries[BuildKey(entry.Schema, entry.Name)] = entry;
            }
        }

        if (schemaArtifacts?.TableTypes != null)
        {
            foreach (var entry in schemaArtifacts.TableTypes)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Schema) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }
                tableTypeEntries[BuildKey(entry.Schema, entry.Name)] = entry;
            }
        }

        var userDefinedTypeEntries = new Dictionary<string, IndexUserDefinedTypeEntry>(StringComparer.OrdinalIgnoreCase);
        if ((schemaArtifacts?.UserDefinedTypes == null || schemaArtifacts.UserDefinedTypes.Count == 0) && existing?.UserDefinedTypes != null)
        {
            foreach (var entry in existing.UserDefinedTypes)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Schema) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }
                userDefinedTypeEntries[BuildKey(entry.Schema, entry.Name)] = entry;
            }
        }

        if (schemaArtifacts?.UserDefinedTypes != null)
        {
            foreach (var entry in schemaArtifacts.UserDefinedTypes)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Schema) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }
                userDefinedTypeEntries[BuildKey(entry.Schema, entry.Name)] = entry;
            }
        }

        var procedureList = entries.Values
            .Where(e => !string.IsNullOrWhiteSpace(e.File))
            .OrderBy(e => e.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tableTypeList = tableTypeEntries.Values
            .Where(e => !string.IsNullOrWhiteSpace(e.File))
            .OrderBy(e => e.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var userDefinedTypeList = userDefinedTypeEntries.Values
            .Where(e => !string.IsNullOrWhiteSpace(e.File))
            .OrderBy(e => e.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parser = existing?.Parser ?? new IndexParser
        {
            ToolVersion = "net9.0",
            ResultSetParserVersion = 8
        };
        var stats = existing?.Stats ?? new IndexStats();
        stats.ProcedureTotal = procedureList.Count;
        var loaded = updated?.Count ?? 0;
        stats.ProcedureLoaded = loaded;
        stats.ProcedureSkipped = Math.Max(0, stats.ProcedureTotal - stats.ProcedureLoaded);
        stats.UdttTotal = tableTypeList.Count;
        stats.UserDefinedTypeTotal = userDefinedTypeList.Count;

        var fingerprintParts = new List<string>();
        fingerprintParts.AddRange(procedureList.Select(p => $"proc:{p.Schema}.{p.Name}:{p.Hash}"));
        fingerprintParts.AddRange(tableTypeList.Select(t => $"tt:{t.Schema}.{t.Name}:{t.Hash}"));
        fingerprintParts.AddRange(userDefinedTypeList.Select(t => $"udt:{t.Schema}.{t.Name}:{t.Hash}"));
        var fingerprintSource = string.Join("|", fingerprintParts);
        var fingerprint = string.IsNullOrEmpty(fingerprintSource) ? string.Empty : ComputeHash(fingerprintSource);

        var document = new IndexDocument
        {
            SchemaVersion = existing?.SchemaVersion ?? 1,
            Fingerprint = fingerprint,
            Parser = parser,
            Stats = stats,
            Procedures = procedureList,
            TableTypes = tableTypeList,
            UserDefinedTypes = userDefinedTypeList
        };

        await using var memoryStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(memoryStream, document, IndexSerializerOptions, cancellationToken).ConfigureAwait(false);
        var newBytes = memoryStream.ToArray();
        var newContent = Encoding.UTF8.GetString(newBytes);

        string? existingContent = null;
        if (File.Exists(indexPath))
        {
            existingContent = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(existingContent, newContent, StringComparison.Ordinal))
        {
            return document;
        }

        await PersistSnapshotAsync(indexPath, newBytes, cancellationToken).ConfigureAwait(false);

        return document;
    }

    private Task WriteLegacySnapshotAsync(IndexDocument? indexDocument, IReadOnlyList<ProcedureAnalysisResult> updatedProcedures, CancellationToken cancellationToken)
    {
        if (_legacySnapshotService == null)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var layoutService = new SchemaSnapshotFileLayoutService();
            var snapshot = layoutService.LoadExpanded();
            if (snapshot == null)
            {
                _console.Verbose("[legacy-bridge] expanded snapshot load returned null; skipping legacy snapshot write");
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(snapshot.Fingerprint) && !string.IsNullOrWhiteSpace(indexDocument?.Fingerprint))
            {
                snapshot.Fingerprint = indexDocument!.Fingerprint;
            }

            if (string.IsNullOrWhiteSpace(snapshot.Fingerprint))
            {
                _console.Verbose("[legacy-bridge] snapshot fingerprint missing; skipping legacy snapshot write");
                return Task.CompletedTask;
            }

            var updatedLookup = BuildUpdatedParameterLookup(updatedProcedures);
            var fallbackLookup = LoadLegacyFallbackSnapshot(snapshot.Fingerprint);

            if (snapshot.Procedures != null)
            {
                foreach (var procedure in snapshot.Procedures)
                {
                    if (procedure == null) continue;
                    var key = BuildKey(procedure.Schema ?? string.Empty, procedure.Name ?? string.Empty);

                    List<SnapshotInput> inputs;
                    if (updatedLookup.TryGetValue(key, out var parameterList))
                    {
                        inputs = ConvertParameters(parameterList);
                    }
                    else if (fallbackLookup.TryGetValue(key, out var legacyInputs))
                    {
                        inputs = legacyInputs.Select(CloneSnapshotInput).ToList();
                    }
                    else if (procedure.Inputs != null && procedure.Inputs.Count > 0)
                    {
                        inputs = procedure.Inputs.Select(CloneSnapshotInput).ToList();
                    }
                    else
                    {
                        inputs = new List<SnapshotInput>();
                    }

                    procedure.Inputs = inputs;
                }
            }

            _legacySnapshotService.Save(snapshot);
            _console.Verbose("[legacy-bridge] legacy snapshot persisted");
        }
        catch (Exception ex)
        {
            _console.Verbose($"[legacy-bridge] failed to persist legacy snapshot: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static Dictionary<string, IReadOnlyList<StoredProcedureInput>> BuildUpdatedParameterLookup(IReadOnlyList<ProcedureAnalysisResult> updatedProcedures)
    {
        var lookup = new Dictionary<string, IReadOnlyList<StoredProcedureInput>>(StringComparer.OrdinalIgnoreCase);
        if (updatedProcedures == null) return lookup;

        foreach (var proc in updatedProcedures)
        {
            var descriptor = proc?.Descriptor;
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Schema) || string.IsNullOrWhiteSpace(descriptor.Name))
            {
                continue;
            }

            var key = BuildKey(descriptor.Schema, descriptor.Name);
            lookup[key] = proc?.Parameters ?? Array.Empty<StoredProcedureInput>();
        }

        return lookup;
    }

    private Dictionary<string, List<SnapshotInput>> LoadLegacyFallbackSnapshot(string currentFingerprint)
    {
        var result = new Dictionary<string, List<SnapshotInput>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var projectRoot = ProjectRootResolver.ResolveCurrent();
            var schemaRoot = Path.Combine(projectRoot, ".spocr", "schema");
            if (!Directory.Exists(schemaRoot)) return result;

            var legacyFiles = Directory.GetFiles(schemaRoot, "*.json", SearchOption.TopDirectoryOnly)
                .Where(f => !string.Equals(Path.GetFileName(f), "index.json", StringComparison.OrdinalIgnoreCase))
                .Where(f => !string.Equals(Path.GetFileNameWithoutExtension(f), currentFingerprint, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();

            foreach (var file in legacyFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var fallback = JsonSerializer.Deserialize<LegacySnapshotDocument>(json, IndexSerializerOptions);
                    if (fallback?.Procedures == null) continue;

                    foreach (var proc in fallback.Procedures)
                    {
                        if (proc == null || string.IsNullOrWhiteSpace(proc.Schema) || string.IsNullOrWhiteSpace(proc.Name)) continue;
                        var key = BuildKey(proc.Schema, proc.Name);
                        if (!result.ContainsKey(key) && proc.Inputs != null)
                        {
                            result[key] = proc.Inputs.Select(CloneSnapshotInput).ToList();
                        }
                    }

                    if (result.Count > 0)
                    {
                        break;
                    }
                }
                catch
                {
                    // continue to next candidate
                }
            }
        }
        catch
        {
            // ignore fallback load errors
        }

        return result;
    }

    private static List<SnapshotInput> ConvertParameters(IReadOnlyList<StoredProcedureInput> parameters)
    {
        var list = new List<SnapshotInput>();
        if (parameters == null) return list;

        foreach (var parameter in parameters)
        {
            if (parameter == null) continue;

            var typeRef = BuildTypeRef(parameter);
            var snapshotInput = new SnapshotInput
            {
                Name = NormalizeParameterName(parameter.Name),
                TypeRef = typeRef,
                IsOutput = parameter.IsOutput ? true : null,
                HasDefaultValue = parameter.HasDefaultValue ? true : null,
                IsNullable = parameter.IsNullable ? true : null,
                MaxLength = parameter.MaxLength > 0 ? parameter.MaxLength : null,
                TableTypeSchema = parameter.IsTableType ? parameter.UserTypeSchemaName : null,
                TableTypeName = parameter.IsTableType ? parameter.UserTypeName : null,
                TypeSchema = !parameter.IsTableType ? parameter.UserTypeSchemaName : null,
                TypeName = !parameter.IsTableType ? parameter.UserTypeName : null,
                Precision = parameter.Precision > 0 ? parameter.Precision : null,
                Scale = parameter.Scale > 0 ? parameter.Scale : null
            };

            if (!parameter.IsTableType && string.IsNullOrWhiteSpace(snapshotInput.TypeName))
            {
                var (_, nameFromRef) = SplitTypeRef(typeRef);
                snapshotInput.TypeSchema ??= "sys";
                snapshotInput.TypeName = nameFromRef;
            }

            if (parameter.IsTableType && string.IsNullOrWhiteSpace(snapshotInput.TableTypeName))
            {
                var (schemaFromRef, nameFromRef) = SplitTypeRef(typeRef);
                snapshotInput.TableTypeSchema ??= schemaFromRef;
                snapshotInput.TableTypeName = nameFromRef;
            }

            list.Add(snapshotInput);
        }

        return list;
    }

    private static SnapshotInput CloneSnapshotInput(SnapshotInput source)
    {
        return new SnapshotInput
        {
            Name = source?.Name ?? string.Empty,
            TypeRef = source?.TypeRef,
            TableTypeSchema = source?.TableTypeSchema,
            TableTypeName = source?.TableTypeName,
            IsNullable = source?.IsNullable,
            MaxLength = source?.MaxLength,
            HasDefaultValue = source?.HasDefaultValue,
            IsOutput = source?.IsOutput,
            TypeSchema = source?.TypeSchema,
            TypeName = source?.TypeName,
            Precision = source?.Precision,
            Scale = source?.Scale
        };
    }

    private static async Task PersistSnapshotAsync(string filePath, byte[] content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tempPath = filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(filePath))
            {
                try
                {
                    File.Replace(tempPath, filePath, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(tempPath, filePath, overwrite: true);
                }
                catch (IOException)
                {
                    File.Copy(tempPath, filePath, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string BuildKey(string schema, string name)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return name ?? string.Empty;
        }

        return $"{schema}.{name}";
    }

    private static byte[] BuildTableTypeJson(TableType tableType, IReadOnlyList<Column> columns)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", tableType?.SchemaName ?? string.Empty);
            writer.WriteString("Name", tableType?.Name ?? string.Empty);
            if (tableType?.UserTypeId.HasValue == true)
            {
                writer.WriteNumber("UserTypeId", tableType.UserTypeId.Value);
            }

            writer.WritePropertyName("Columns");
            writer.WriteStartArray();
            if (columns != null)
            {
                foreach (var column in columns)
                {
                    if (column == null) continue;

                    writer.WriteStartObject();
                    if (!string.IsNullOrWhiteSpace(column.Name))
                    {
                        writer.WriteString("Name", column.Name);
                    }

                    var columnTypeRef = BuildTypeRef(column);
                    if (!string.IsNullOrWhiteSpace(columnTypeRef))
                    {
                        writer.WriteString("TypeRef", columnTypeRef);
                    }
                    else if (!string.IsNullOrWhiteSpace(column.SqlTypeName))
                    {
                        // Fallback for legacy materialized snapshots without TypeRef information.
                        writer.WriteString("SqlTypeName", column.SqlTypeName);
                    }

                    if (column.IsNullable)
                    {
                        writer.WriteBoolean("IsNullable", true);
                    }
                    if (column.MaxLength > 0)
                    {
                        writer.WriteNumber("MaxLength", column.MaxLength);
                    }
                    if (column.Precision.HasValue && column.Precision.Value > 0)
                    {
                        writer.WriteNumber("Precision", column.Precision.Value);
                    }
                    if (column.Scale.HasValue && column.Scale.Value > 0)
                    {
                        writer.WriteNumber("Scale", column.Scale.Value);
                    }
                    if (column.IsIdentityRaw.HasValue && column.IsIdentityRaw.Value == 1)
                    {
                        writer.WriteBoolean("IsIdentity", true);
                    }
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static byte[] BuildScalarTypeJson(UserDefinedTypeRow type)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", type?.schema_name ?? string.Empty);
            writer.WriteString("Name", type?.user_type_name ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(type?.base_type_name))
            {
                writer.WriteString("BaseSqlTypeName", type.base_type_name);
            }
            if (type?.max_length > 0)
            {
                writer.WriteNumber("MaxLength", type.max_length);
            }
            if (type?.precision > 0)
            {
                writer.WriteNumber("Precision", type.precision);
            }
            if (type?.scale > 0)
            {
                writer.WriteNumber("Scale", type.scale);
            }
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string BuildArtifactFileName(string schema, string name)
    {
        var schemaSafe = NameSanitizer.SanitizeForFile(schema ?? string.Empty);
        var nameSafe = NameSanitizer.SanitizeForFile(name ?? string.Empty);
        if (string.IsNullOrWhiteSpace(schemaSafe))
        {
            return string.IsNullOrWhiteSpace(nameSafe) ? "artifact.json" : $"{nameSafe}.json";
        }

        if (string.IsNullOrWhiteSpace(nameSafe))
        {
            return $"{schemaSafe}.json";
        }

        return $"{schemaSafe}.{nameSafe}.json";
    }

    private async Task<ArtifactWriteOutcome> WriteArtifactAsync(string filePath, byte[] content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hash = ComputeHash(content);
        var shouldWrite = true;

        if (File.Exists(filePath))
        {
            try
            {
                var existingBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
                var existingHash = ComputeHash(existingBytes);
                if (string.Equals(existingHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    shouldWrite = false;
                }
            }
            catch (Exception ex)
            {
                _console.Verbose($"[snapshot-write] failed to read existing snapshot at {filePath}: {ex.Message}");
            }
        }

        if (shouldWrite)
        {
            await PersistSnapshotAsync(filePath, content, cancellationToken).ConfigureAwait(false);
        }

        return new ArtifactWriteOutcome(shouldWrite, hash);
    }

    private static void PruneExtraneousFiles(string directory, HashSet<string> validFileNames)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var existingFiles = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var path in existingFiles)
            {
                var fileName = Path.GetFileName(path);
                if (validFileNames != null && validFileNames.Contains(fileName))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private sealed record ArtifactWriteOutcome(bool Wrote, string Hash);

    private sealed record ProcedureWriteRecord(int Index, ProcedureAnalysisResult Result, bool Wrote);

    private sealed class SchemaArtifactSummary
    {
        public int FilesWritten { get; set; }
        public int FilesUnchanged { get; set; }
        public List<IndexTableTypeEntry> TableTypes { get; } = new();
        public List<IndexUserDefinedTypeEntry> UserDefinedTypes { get; } = new();
    }

    private sealed class LegacySnapshotDocument
    {
        public int SchemaVersion { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public List<LegacyProcedureDocument> Procedures { get; set; } = new();
    }

    private sealed class LegacyProcedureDocument
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<SnapshotInput> Inputs { get; set; } = new();
    }

    private sealed class IndexDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public string Fingerprint { get; set; } = string.Empty;
        public IndexParser Parser { get; set; } = new();
        public IndexStats Stats { get; set; } = new();
        public List<IndexProcedureEntry> Procedures { get; set; } = new();
        public List<IndexTableTypeEntry> TableTypes { get; set; } = new();
        public List<IndexUserDefinedTypeEntry> UserDefinedTypes { get; set; } = new();
    }

    private sealed class IndexParser
    {
        public string ToolVersion { get; set; } = string.Empty;
        public int ResultSetParserVersion { get; set; }
    }

    private sealed class IndexStats
    {
        public int ProcedureTotal { get; set; }
        public int ProcedureSkipped { get; set; }
        public int ProcedureLoaded { get; set; }
        public int UdttTotal { get; set; }
        public int TableTotal { get; set; }
        public int ViewTotal { get; set; }
        public int UserDefinedTypeTotal { get; set; }
    }

    private sealed class IndexProcedureEntry
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
    }

    private sealed class IndexTableTypeEntry
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
    }

    private sealed class IndexUserDefinedTypeEntry
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
    }
}

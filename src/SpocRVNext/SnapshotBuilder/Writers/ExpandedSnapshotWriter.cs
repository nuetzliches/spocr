using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Models;
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
    private static readonly JsonSerializerOptions IndexSerializerOptions = new()
    {
        WriteIndented = true
    };

    public ExpandedSnapshotWriter(IConsoleService console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public async Task<SnapshotWriteResult> WriteAsync(IReadOnlyList<ProcedureAnalysisResult> analyzedProcedures, SnapshotBuildOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        foreach (var item in analyzedProcedures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item == null) continue;

            var descriptor = item.Descriptor ?? new ProcedureDescriptor();
            var fileName = string.IsNullOrWhiteSpace(item.SnapshotFile)
                ? BuildDefaultSnapshotFile(descriptor)
                : item.SnapshotFile;
            var filePath = Path.Combine(proceduresRoot, fileName);

            var json = BuildProcedureJson(descriptor, item.Inputs, item.Ast);
            var hash = ComputeHash(json);
            var shouldWrite = true;

            if (File.Exists(filePath))
            {
                try
                {
                    var existing = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                    if (string.Equals(existing, json, StringComparison.Ordinal))
                    {
                        shouldWrite = false;
                    }
                }
                catch (Exception readEx)
                {
                    _console.Verbose($"[snapshot-write] failed to read existing snapshot for {descriptor}: {readEx.Message}");
                }
            }

            if (shouldWrite)
            {
                await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
                filesWritten++;
                if (verbose)
                {
                    _console.Verbose($"[snapshot-write] wrote {fileName}");
                }
            }
            else
            {
                filesUnchanged++;
            }

            updated.Add(new ProcedureAnalysisResult
            {
                Descriptor = descriptor,
                Ast = item.Ast,
                WasReusedFromCache = item.WasReusedFromCache,
                SourceLastModifiedUtc = item.SourceLastModifiedUtc,
                SnapshotFile = fileName,
                SnapshotHash = hash,
                Inputs = item.Inputs,
                Dependencies = item.Dependencies
            });
        }

        await UpdateIndexAsync(schemaRoot, updated, cancellationToken).ConfigureAwait(false);

        return new SnapshotWriteResult
        {
            FilesWritten = filesWritten,
            FilesUnchanged = filesUnchanged,
            UpdatedProcedures = updated
        };
    }

    private static string BuildProcedureJson(ProcedureDescriptor descriptor, IReadOnlyList<StoredProcedureInput> inputs, StoredProcedureContentModel? ast)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", descriptor?.Schema ?? string.Empty);
            writer.WriteString("Name", descriptor?.Name ?? string.Empty);

            WriteInputs(writer, inputs);
            WriteResultSets(writer, ast?.ResultSets);

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteInputs(Utf8JsonWriter writer, IReadOnlyList<StoredProcedureInput> inputs)
    {
        writer.WritePropertyName("Inputs");
        writer.WriteStartArray();
        if (inputs != null)
        {
            foreach (var input in inputs)
            {
                if (input == null) continue;

                writer.WriteStartObject();
                var name = NormalizeParameterName(input.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    writer.WriteString("Name", name);
                }

                if (input.IsTableType || (!string.IsNullOrWhiteSpace(input.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(input.UserTypeName)))
                {
                    if (!string.IsNullOrWhiteSpace(input.UserTypeSchemaName))
                    {
                        writer.WriteString("TableTypeSchema", input.UserTypeSchemaName);
                    }
                    if (!string.IsNullOrWhiteSpace(input.UserTypeName))
                    {
                        writer.WriteString("TableTypeName", input.UserTypeName);
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(input.SqlTypeName))
                    {
                        writer.WriteString("SqlTypeName", input.SqlTypeName);
                    }
                    if (input.IsNullable)
                    {
                        writer.WriteBoolean("IsNullable", true);
                    }
                    if (input.MaxLength > 0)
                    {
                        writer.WriteNumber("MaxLength", input.MaxLength);
                    }
                    if (!string.IsNullOrWhiteSpace(input.BaseSqlTypeName) && !string.Equals(input.BaseSqlTypeName, input.SqlTypeName, StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteString("BaseSqlTypeName", input.BaseSqlTypeName);
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

    private static void WriteResultColumn(Utf8JsonWriter writer, StoredProcedureContentModel.ResultColumn column)
    {
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(column.Name))
        {
            writer.WriteString("Name", column.Name);
        }
        var sqlType = column.SqlTypeName;
        if (string.IsNullOrWhiteSpace(sqlType) && !string.IsNullOrWhiteSpace(column.CastTargetType))
        {
            sqlType = column.CastTargetType;
        }
        if (!string.IsNullOrWhiteSpace(sqlType))
        {
            writer.WriteString("SqlTypeName", sqlType);
        }
        if (column.IsNullable == true)
        {
            writer.WriteBoolean("IsNullable", true);
        }
        if (column.MaxLength.HasValue && column.MaxLength.Value > 0)
        {
            writer.WriteNumber("MaxLength", column.MaxLength.Value);
        }
        if (!string.IsNullOrWhiteSpace(column.UserTypeSchemaName))
        {
            writer.WriteString("UserTypeSchemaName", column.UserTypeSchemaName);
        }
        if (!string.IsNullOrWhiteSpace(column.UserTypeName))
        {
            writer.WriteString("UserTypeName", column.UserTypeName);
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

    private static string BuildDefaultSnapshotFile(ProcedureDescriptor descriptor)
    {
        var schema = string.IsNullOrWhiteSpace(descriptor?.Schema) ? "unknown" : descriptor.Schema;
        var name = string.IsNullOrWhiteSpace(descriptor?.Name) ? "unnamed" : descriptor.Name;
        return $"{schema}.{name}.json";
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 16);
    }

    private static async Task UpdateIndexAsync(string schemaRoot, IReadOnlyList<ProcedureAnalysisResult> updated, CancellationToken cancellationToken)
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

        var procedureList = entries.Values
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

        var fingerprintSource = string.Join("|", procedureList.Select(p => $"{p.Schema}.{p.Name}:{p.Hash}"));
        var fingerprint = string.IsNullOrEmpty(fingerprintSource) ? string.Empty : ComputeHash(fingerprintSource);

        var document = new IndexDocument
        {
            SchemaVersion = existing?.SchemaVersion ?? 1,
            Fingerprint = fingerprint,
            Parser = parser,
            Stats = stats,
            Procedures = procedureList
        };

        var tempPath = indexPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, IndexSerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        string? existingContent = null;
        if (File.Exists(indexPath))
        {
            existingContent = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
        }
        var newContent = await File.ReadAllTextAsync(tempPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(existingContent, newContent, StringComparison.Ordinal))
        {
            if (File.Exists(indexPath))
            {
                File.Replace(tempPath, indexPath, null);
            }
            else
            {
                File.Move(tempPath, indexPath);
            }
        }
        else
        {
            File.Delete(tempPath);
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

    private sealed class IndexDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public string Fingerprint { get; set; } = string.Empty;
        public IndexParser Parser { get; set; } = new();
        public IndexStats Stats { get; set; } = new();
        public List<IndexProcedureEntry> Procedures { get; set; } = new();
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
}

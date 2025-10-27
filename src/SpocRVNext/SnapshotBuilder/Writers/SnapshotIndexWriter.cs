using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

internal sealed class SnapshotIndexWriter
{
    private static readonly JsonSerializerOptions IndexSerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly Func<string, byte[], CancellationToken, Task> _persistSnapshotAsync;

    public SnapshotIndexWriter(Func<string, byte[], CancellationToken, Task> persistSnapshotAsync)
    {
        _persistSnapshotAsync = persistSnapshotAsync ?? throw new ArgumentNullException(nameof(persistSnapshotAsync));
    }

    public async Task<IndexDocument> UpdateAsync(
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

                entries[SnapshotWriterUtilities.BuildKey(entry.Schema, entry.Name)] = entry;
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

            var key = SnapshotWriterUtilities.BuildKey(descriptor.Schema, descriptor.Name);
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

                tableTypeEntries[SnapshotWriterUtilities.BuildKey(entry.Schema, entry.Name)] = entry;
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

                tableTypeEntries[SnapshotWriterUtilities.BuildKey(entry.Schema, entry.Name)] = entry;
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

                userDefinedTypeEntries[SnapshotWriterUtilities.BuildKey(entry.Schema, entry.Name)] = entry;
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

                userDefinedTypeEntries[SnapshotWriterUtilities.BuildKey(entry.Schema, entry.Name)] = entry;
            }
        }

        var functionEntries = new Dictionary<string, IndexFunctionEntry>(StringComparer.OrdinalIgnoreCase);
        var functionsVersion = existing?.FunctionsVersion ?? 0;
        var hasNewFunctionData = (schemaArtifacts?.FunctionsVersion ?? 0) > 0;

        if (!hasNewFunctionData && (schemaArtifacts?.Functions == null || schemaArtifacts.Functions.Count == 0) && existing?.Functions != null)
        {
            foreach (var entry in existing.Functions)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Schema) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                functionEntries[SnapshotWriterUtilities.BuildKey(entry.Schema, entry.Name)] = entry;
            }
        }

        if (schemaArtifacts?.Functions != null)
        {
            foreach (var entry in schemaArtifacts.Functions)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Schema) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                functionEntries[SnapshotWriterUtilities.BuildKey(entry.Schema, entry.Name)] = entry;
            }
        }

        if (hasNewFunctionData)
        {
            functionsVersion = schemaArtifacts!.FunctionsVersion;
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

        var functionList = functionEntries.Values
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
        stats.FunctionTotal = functionList.Count;

        var fingerprintParts = new List<string>();
        fingerprintParts.AddRange(procedureList.Select(p => $"proc:{p.Schema}.{p.Name}:{p.Hash}"));
        fingerprintParts.AddRange(tableTypeList.Select(t => $"tt:{t.Schema}.{t.Name}:{t.Hash}"));
        fingerprintParts.AddRange(userDefinedTypeList.Select(t => $"udt:{t.Schema}.{t.Name}:{t.Hash}"));
        fingerprintParts.AddRange(functionList.Select(f => $"fn:{f.Schema}.{f.Name}:{f.Hash}"));
        var fingerprintSource = string.Join("|", fingerprintParts);
        var fingerprint = string.IsNullOrEmpty(fingerprintSource) ? string.Empty : SnapshotWriterUtilities.ComputeHash(fingerprintSource);

        var document = new IndexDocument
        {
            SchemaVersion = existing?.SchemaVersion ?? 1,
            Fingerprint = fingerprint,
            Parser = parser,
            Stats = stats,
            Procedures = procedureList,
            TableTypes = tableTypeList,
            UserDefinedTypes = userDefinedTypeList,
            FunctionsVersion = functionsVersion,
            Functions = functionList
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

        if (!string.Equals(existingContent, newContent, StringComparison.Ordinal))
        {
            await _persistSnapshotAsync(indexPath, newBytes, cancellationToken).ConfigureAwait(false);
        }

        return document;
    }
}

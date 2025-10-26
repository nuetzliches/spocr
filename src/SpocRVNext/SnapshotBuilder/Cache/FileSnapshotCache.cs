using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpocR.Services;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.SpocRVNext.Utils;
using SnapshotProcedureCacheEntry = SpocR.SpocRVNext.SnapshotBuilder.Models.ProcedureCacheEntry;
using SnapshotProcedureDependency = SpocR.SpocRVNext.SnapshotBuilder.Models.ProcedureDependency;

namespace SpocR.SpocRVNext.SnapshotBuilder.Cache;

/// <summary>
/// Persists procedure snapshot fingerprints under .spocr/cache/procedures.json to enable fast reuse decisions.
/// </summary>
internal sealed class FileSnapshotCache : ISnapshotCache
{
    private readonly IConsoleService _console;
    private readonly object _sync = new();
    private readonly Dictionary<string, SnapshotProcedureCacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private bool _initialized;
    private bool _enabled;
    private bool _dirty;
    private string _cacheDirectory = string.Empty;
    private string _cacheFilePath = string.Empty;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileSnapshotCache(IConsoleService console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public async Task InitializeAsync(SnapshotBuildOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_initialized) return;

        _enabled = options?.NoCache != true;
        _initialized = true;
        if (!_enabled)
        {
            return;
        }

        var projectRoot = ProjectRootResolver.ResolveCurrent();
        _cacheDirectory = Path.Combine(projectRoot, ".spocr", "cache");
        _cacheFilePath = Path.Combine(_cacheDirectory, "procedures.json");

        if (!File.Exists(_cacheFilePath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_cacheFilePath);
            var document = await JsonSerializer.DeserializeAsync<ProcedureCacheDocument>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (document?.Entries == null || document.Entries.Count == 0)
            {
                return;
            }

            lock (_sync)
            {
                _entries.Clear();
                foreach (var entry in document.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Schema) || string.IsNullOrWhiteSpace(entry.Name))
                    {
                        continue;
                    }

                    var key = BuildKey(entry.Schema, entry.Name);
                    _entries[key] = new SnapshotProcedureCacheEntry
                    {
                        Schema = entry.Schema,
                        Name = entry.Name,
                        LastModifiedUtc = NormalizeUtc(entry.LastModifiedUtc),
                        SnapshotHash = entry.SnapshotHash,
                        SnapshotFile = entry.SnapshotFile,
                        LastAnalyzedUtc = NormalizeUtc(entry.LastAnalyzedUtc),
                        Dependencies = entry.Dependencies?
                            .Select(d => new SnapshotProcedureDependency
                            {
                                Kind = d.Kind,
                                Schema = d.Schema,
                                Name = d.Name,
                                LastModifiedUtc = NormalizeUtc(d.LastModifiedUtc ?? default)
                            })
                            .ToList() ?? new List<SnapshotProcedureDependency>()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-cache] failed to load procedures.json ({ex.Message})");
        }
    }

    public SnapshotProcedureCacheEntry? TryGetProcedure(ProcedureDescriptor descriptor)
    {
        if (!_enabled || descriptor == null)
        {
            return null;
        }

        var key = BuildKey(descriptor.Schema, descriptor.Name);
        lock (_sync)
        {
            return _entries.TryGetValue(key, out var cached) ? cached : null;
        }
    }

    public Task RecordReuseAsync(ProcedureCollectionItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled || item == null)
        {
            return Task.CompletedTask;
        }

        var descriptor = item.Descriptor ?? new ProcedureDescriptor();
        var key = BuildKey(descriptor.Schema, descriptor.Name);

        lock (_sync)
        {
            var existing = _entries.TryGetValue(key, out var current) ? current : null;
            var lastModified = NormalizeUtc(item.LastModifiedUtc ?? existing?.LastModifiedUtc ?? default);
            var entry = new SnapshotProcedureCacheEntry
            {
                Schema = descriptor.Schema,
                Name = descriptor.Name,
                LastModifiedUtc = lastModified,
                SnapshotHash = item.CachedSnapshotHash ?? existing?.SnapshotHash,
                SnapshotFile = item.CachedSnapshotFile ?? existing?.SnapshotFile ?? BuildDefaultSnapshotFile(descriptor),
                LastAnalyzedUtc = DateTime.UtcNow,
                Dependencies = existing?.Dependencies ?? Array.Empty<SnapshotProcedureDependency>()
            };
            _entries[key] = entry;
            _dirty = true;
        }

        return Task.CompletedTask;
    }

    public Task RecordAnalysisAsync(ProcedureAnalysisResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled || result == null)
        {
            return Task.CompletedTask;
        }

        var descriptor = result.Descriptor ?? new ProcedureDescriptor();
        var key = BuildKey(descriptor.Schema, descriptor.Name);
        var lastModified = NormalizeUtc(result.SourceLastModifiedUtc ?? default);

        lock (_sync)
        {
            var existing = _entries.TryGetValue(key, out var current) ? current : null;
            var entry = new SnapshotProcedureCacheEntry
            {
                Schema = descriptor.Schema,
                Name = descriptor.Name,
                LastModifiedUtc = lastModified != default ? lastModified : existing?.LastModifiedUtc ?? default,
                SnapshotHash = result.SnapshotHash ?? existing?.SnapshotHash,
                SnapshotFile = result.SnapshotFile ?? existing?.SnapshotFile ?? BuildDefaultSnapshotFile(descriptor),
                LastAnalyzedUtc = DateTime.UtcNow,
                Dependencies = (result.Dependencies ?? Array.Empty<SnapshotProcedureDependency>()).ToList()
            };
            _entries[key] = entry;
            _dirty = true;
        }

        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled || !_dirty)
        {
            return Task.CompletedTask;
        }

        List<ProcedureCacheEntryRecord> entries;
        lock (_sync)
        {
            entries = _entries.Values
                .Select(e => new ProcedureCacheEntryRecord
                {
                    Schema = e.Schema,
                    Name = e.Name,
                    LastModifiedUtc = e.LastModifiedUtc,
                    SnapshotHash = e.SnapshotHash,
                    SnapshotFile = e.SnapshotFile,
                    LastAnalyzedUtc = e.LastAnalyzedUtc,
                    Dependencies = (e.Dependencies ?? Array.Empty<SnapshotProcedureDependency>())
                        .Select(d => new ProcedureCacheDependencyRecord
                        {
                            Kind = d.Kind,
                            Schema = d.Schema,
                            Name = d.Name,
                            LastModifiedUtc = d.LastModifiedUtc
                        })
                        .OrderBy(d => d.Kind)
                        .ThenBy(d => d.Schema, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .OrderBy(e => e.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var document = new ProcedureCacheDocument
            {
                Version = 1,
                Entries = entries
            };

            var tempFile = _cacheFilePath + ".tmp";
            using (var stream = File.Create(tempFile))
            {
                JsonSerializer.Serialize(stream, document, SerializerOptions);
            }

            if (File.Exists(_cacheFilePath))
            {
                File.Replace(tempFile, _cacheFilePath, null);
            }
            else
            {
                File.Move(tempFile, _cacheFilePath);
            }

            _dirty = false;
        }
        catch (Exception ex)
        {
            _dirty = true;
            _console.Verbose($"[snapshot-cache] failed to persist procedures.json ({ex.Message})");
        }

        return Task.CompletedTask;
    }

    private static string BuildKey(string schema, string name)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return name ?? string.Empty;
        }

        return $"{schema}.{name}";
    }

    private static string BuildDefaultSnapshotFile(ProcedureDescriptor descriptor)
    {
        var schema = string.IsNullOrWhiteSpace(descriptor?.Schema) ? "unknown" : descriptor.Schema;
        var name = string.IsNullOrWhiteSpace(descriptor?.Name) ? "unnamed" : descriptor.Name;
        return $"{schema}.{name}.json";
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        if (value == default)
        {
            return default;
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private sealed class ProcedureCacheDocument
    {
        public int Version { get; set; }
        public List<ProcedureCacheEntryRecord> Entries { get; set; } = new();
    }

    private sealed class ProcedureCacheEntryRecord
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime LastModifiedUtc { get; set; }
        public string? SnapshotHash { get; set; }
        public string? SnapshotFile { get; set; }
        public DateTime LastAnalyzedUtc { get; set; }
        public List<ProcedureCacheDependencyRecord> Dependencies { get; set; } = new();
    }

    private sealed class ProcedureCacheDependencyRecord
    {
        public ProcedureDependencyKind Kind { get; set; }
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime? LastModifiedUtc { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Data;
using SpocR.SpocRVNext.Data.Models;
using SpocR.SpocRVNext.Data.Queries;
using SpocR.SpocRVNext.Services;
using SpocR.SpocRVNext.SnapshotBuilder.Cache;
using SpocR.SpocRVNext.SnapshotBuilder.Metadata;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Collectors;

internal sealed class DatabaseProcedureCollector : IProcedureCollector
{
    private readonly DbContext _dbContext;
    private readonly IConsoleService _console;
    private readonly ISnapshotCache _cache;
    private readonly IDependencyMetadataProvider _dependencyMetadataProvider;

    public DatabaseProcedureCollector(DbContext dbContext, IConsoleService console, ISnapshotCache cache, IDependencyMetadataProvider dependencyMetadataProvider)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dependencyMetadataProvider = dependencyMetadataProvider ?? throw new ArgumentNullException(nameof(dependencyMetadataProvider));
    }

    public async Task<ProcedureCollectionResult> CollectAsync(SnapshotBuildOptions options, CancellationToken cancellationToken)
    {
        options ??= SnapshotBuildOptions.Default;
        cancellationToken.ThrowIfCancellationRequested();

        var allProcedures = await _dbContext.StoredProcedureListAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        if (allProcedures == null || allProcedures.Count == 0)
        {
            _console.Verbose("[snapshot-collect] No procedures returned by catalog query");
            return new ProcedureCollectionResult
            {
                Items = Array.Empty<ProcedureCollectionItem>()
            };
        }

        var procedureMap = new Dictionary<string, StoredProcedure>(StringComparer.OrdinalIgnoreCase);
        foreach (var procedure in allProcedures)
        {
            var key = BuildProcedureKey(procedure.SchemaName, procedure.Name);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            procedureMap[key] = procedure;
        }

        var schemaFilter = BuildSchemaFilter(options.Schemas);
        var matcher = BuildProcedureMatcher(options.ProcedureWildcard, out var explicitProcedureRequests);

        var seedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var procedure in allProcedures)
        {
            if (schemaFilter != null && schemaFilter.Count > 0 && !schemaFilter.Contains(procedure.SchemaName))
            {
                continue;
            }

            if (matcher != null && !matcher(procedure.SchemaName, procedure.Name))
            {
                continue;
            }

            seedKeys.Add(BuildProcedureKey(procedure.SchemaName, procedure.Name));
        }

        if (seedKeys.Count == 0)
        {
            if ((schemaFilter == null || schemaFilter.Count == 0) && matcher == null)
            {
                // No filters supplied but catalog also empty.
                _console.Verbose("[snapshot-collect] No stored procedures available to process");
            }
            else
            {
                _console.Verbose("[snapshot-collect] No stored procedures matched configured filters");
            }

            return new ProcedureCollectionResult
            {
                Items = Array.Empty<ProcedureCollectionItem>()
            };
        }

        var selectedKeys = new HashSet<string>(seedKeys, StringComparer.OrdinalIgnoreCase);
        var dependencyCount = 0;

        if (selectedKeys.Count > 0)
        {
            var dependencyEdges = await _dbContext.StoredProcedureDependencyListAsync(cancellationToken).ConfigureAwait(false);
            if (dependencyEdges?.Count > 0)
            {
                var adjacency = BuildAdjacency(dependencyEdges);
                var queue = new Queue<string>(seedKeys);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!adjacency.TryGetValue(current, out var neighbors))
                    {
                        continue;
                    }

                    foreach (var neighbor in neighbors)
                    {
                        if (!procedureMap.ContainsKey(neighbor))
                        {
                            continue;
                        }

                        if (selectedKeys.Add(neighbor))
                        {
                            dependencyCount++;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }
        }

        var procedures = allProcedures
            .Where(p => selectedKeys.Contains(BuildProcedureKey(p.SchemaName, p.Name)))
            .ToList();

        var items = new List<ProcedureCollectionItem>(procedures.Count);
        foreach (var procedure in procedures)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var descriptor = new ProcedureDescriptor
            {
                Schema = procedure.SchemaName,
                Name = procedure.Name
            };

            var lastModifiedUtc = NormalizeUtc(procedure.Modified);
            var decision = ProcedureCollectionDecision.Analyze;
            string? cachedHash = null;
            string? cachedFile = null;
            IReadOnlyList<ProcedureDependency> cachedDependencies = Array.Empty<ProcedureDependency>();

            if (!options.NoCache)
            {
                var cachedEntry = _cache.TryGetProcedure(descriptor);
                if (cachedEntry != null)
                {
                    cachedHash = cachedEntry.SnapshotHash;
                    cachedFile = cachedEntry.SnapshotFile;
                    cachedDependencies = cachedEntry.Dependencies ?? Array.Empty<ProcedureDependency>();

                    if (cachedEntry.LastModifiedUtc >= lastModifiedUtc)
                    {
                        decision = ProcedureCollectionDecision.Reuse;

                        if (cachedDependencies.Count > 0)
                        {
                            var currentDeps = await _dependencyMetadataProvider.ResolveAsync(cachedDependencies, cancellationToken).ConfigureAwait(false);
                            if (HasDependencyChanged(cachedDependencies, currentDeps))
                            {
                                _console.Verbose($"[snapshot-collect] dependency delta detected for {descriptor.Schema}.{descriptor.Name}");
                                decision = ProcedureCollectionDecision.Analyze;
                            }
                        }
                    }
                    else
                    {
                        _console.Verbose($"[snapshot-collect] lastModified mismatch for {descriptor.Schema}.{descriptor.Name}: cached={cachedEntry.LastModifiedUtc:O}, current={lastModifiedUtc:O}");
                    }
                }
                else
                {
                    _console.Verbose($"[snapshot-collect] cache miss for {descriptor.Schema}.{descriptor.Name}");
                }
            }

            items.Add(new ProcedureCollectionItem
            {
                Descriptor = descriptor,
                Decision = decision,
                LastModifiedUtc = lastModifiedUtc,
                CachedSnapshotHash = cachedHash,
                CachedSnapshotFile = cachedFile,
                CachedDependencies = cachedDependencies
            });
        }

        if (explicitProcedureRequests.Count > 0)
        {
            foreach (var requestedKey in explicitProcedureRequests)
            {
                if (procedureMap.ContainsKey(requestedKey))
                {
                    continue;
                }

                var descriptor = BuildDescriptorFromKey(requestedKey);
                items.Add(new ProcedureCollectionItem
                {
                    Descriptor = descriptor,
                    Decision = ProcedureCollectionDecision.Skip
                });
                _console.Verbose($"[snapshot-collect] Requested procedure '{requestedKey}' not found (marked as skip).");
            }
        }

        var reuseCount = items.Count(static i => i.Decision == ProcedureCollectionDecision.Reuse);
        var analyzeCount = items.Count(static i => i.Decision == ProcedureCollectionDecision.Analyze);
        var skipCount = items.Count(static i => i.Decision == ProcedureCollectionDecision.Skip);

        if (reuseCount > 0 || skipCount > 0)
        {
            var tagReuse = reuseCount > 0 ? $"; reuse={reuseCount}" : string.Empty;
            var tagSkip = skipCount > 0 ? $"; skip={skipCount}" : string.Empty;
            _console.Verbose($"[snapshot-collect] Collected {items.Count} procedure(s); seeds={seedKeys.Count}; deps={dependencyCount}{tagReuse}{tagSkip}; analyze={analyzeCount}");
        }
        else
        {
            _console.Verbose($"[snapshot-collect] Collected {items.Count} procedure(s); seeds={seedKeys.Count}; deps={dependencyCount}");
        }

        return new ProcedureCollectionResult
        {
            Items = items
        };
    }

    private static ProcedureDescriptor BuildDescriptorFromKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new ProcedureDescriptor();
        }

        var schema = string.Empty;
        var name = key;
        var separatorIndex = key.IndexOf('.');
        if (separatorIndex >= 0)
        {
            schema = key[..separatorIndex];
            name = key[(separatorIndex + 1)..];
        }

        return new ProcedureDescriptor
        {
            Schema = schema,
            Name = name
        };
    }

    private static bool HasDependencyChanged(IReadOnlyList<ProcedureDependency> cachedDependencies, IReadOnlyList<ProcedureDependency> currentDependencies)
    {
        if (cachedDependencies == null || cachedDependencies.Count == 0)
        {
            return false;
        }

        if (currentDependencies == null || currentDependencies.Count == 0)
        {
            return true;
        }

        var currentMap = currentDependencies.ToDictionary(
            d => BuildDependencyKey(d),
            d => d.LastModifiedUtc,
            StringComparer.OrdinalIgnoreCase);

        foreach (var cached in cachedDependencies)
        {
            var key = BuildDependencyKey(cached);
            if (!currentMap.TryGetValue(key, out var currentModified))
            {
                return true;
            }

            var cachedModified = cached.LastModifiedUtc;
            if (cachedModified.HasValue && currentModified.HasValue)
            {
                if (currentModified.Value != cachedModified.Value)
                {
                    return true;
                }
            }
            else if (cachedModified.HasValue != currentModified.HasValue)
            {
                return true;
            }
        }

        return false;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string BuildDependencyKey(ProcedureDependency dependency)
    {
        if (dependency == null) return string.Empty;
        var kind = dependency.Kind.ToString();
        var schema = dependency.Schema ?? string.Empty;
        var name = dependency.Name ?? string.Empty;
        return $"{kind}|{schema}|{name}";
    }

    private static HashSet<string>? BuildSchemaFilter(IReadOnlyList<string> schemas)
    {
        if (schemas == null || schemas.Count == 0) return null;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in schemas)
        {
            if (string.IsNullOrWhiteSpace(schema))
            {
                continue;
            }

            var normalized = schema.Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            set.Add(normalized);
        }

        return set.Count == 0 ? null : set;
    }

    private static string BuildProcedureKey(string? schema, string? name)
    {
        var normalizedSchema = string.IsNullOrWhiteSpace(schema) ? string.Empty : schema.Trim();
        var normalizedName = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        return normalizedSchema.Length == 0 ? normalizedName : $"{normalizedSchema}.{normalizedName}";
    }

    private static Dictionary<string, HashSet<string>> BuildAdjacency(IEnumerable<StoredProcedureDependencyEdge> edges)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (edges == null)
        {
            return map;
        }

        foreach (var edge in edges)
        {
            if (edge == null)
            {
                continue;
            }

            var sourceKey = BuildProcedureKey(edge.ReferencingSchemaName, edge.ReferencingName);
            var targetKey = BuildProcedureKey(edge.ReferencedSchemaName, edge.ReferencedName);
            if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(targetKey))
            {
                continue;
            }

            if (!map.TryGetValue(sourceKey, out var targets))
            {
                targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[sourceKey] = targets;
            }

            targets.Add(targetKey);
        }

        return map;
    }

    private static Func<string, string, bool>? BuildProcedureMatcher(string? raw, out HashSet<string> explicitRequests)
    {
        explicitRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var tokens = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim())
                        .Where(t => t.Length > 0)
                        .ToList();
        if (tokens.Count == 0) return null;

        var regexes = new List<Regex>();
        foreach (var token in tokens)
        {
            if (token.Contains('*') || token.Contains('?'))
            {
                var escaped = Regex.Escape(token);
                var pattern = "^" + escaped.Replace("\\*", ".*").Replace("\\?", ".") + "$";
                try { regexes.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)); }
                catch { }
            }
            else
            {
                var (schema, name) = ParseProcedureToken(token);
                var key = BuildProcedureKey(schema, name);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    explicitRequests.Add(key);
                }
            }
        }
        if (explicitRequests.Count == 0 && regexes.Count == 0) return null;

        var exactRequests = explicitRequests;
        return (schema, name) =>
        {
            var key = BuildProcedureKey(schema, name);
            if (!string.IsNullOrWhiteSpace(key) && exactRequests.Contains(key)) return true;
            if (regexes.Count == 0) return false;
            var fq = string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";
            foreach (var rx in regexes)
            {
                if (rx.IsMatch(fq)) return true;
            }
            return false;
        };
    }

    private static (string Schema, string Name) ParseProcedureToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (string.Empty, string.Empty);
        }

        var parts = token.Split('.', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return (parts[0], parts[1]);
        }

        return (string.Empty, parts.Length == 1 ? parts[0] : string.Empty);
    }
}

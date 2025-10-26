using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpocR.DataContext;
using SpocR.Services;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

internal sealed class DatabaseFunctionJsonMetadataProvider : IFunctionJsonMetadataProvider
{
    private readonly DbContext _dbContext;
    private readonly IConsoleService _console;
    private readonly Dictionary<string, FunctionJsonMetadata?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    public DatabaseFunctionJsonMetadataProvider(DbContext dbContext, IConsoleService console)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public async Task<FunctionJsonMetadata?> ResolveAsync(string? schema, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var normalizedSchema = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema.Trim();
        var key = string.Concat(normalizedSchema, ".", name.Trim());

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var resolved = await ResolveInternalAsync(normalizedSchema, name.Trim(), cancellationToken).ConfigureAwait(false);

        lock (_cacheLock)
        {
            _cache[key] = resolved;
        }

        return resolved;
    }

    private async Task<FunctionJsonMetadata?> ResolveInternalAsync(string schema, string name, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT TOP 1 OBJECT_DEFINITION(o.object_id) AS Definition
FROM sys.objects AS o
INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE s.name = @schema AND o.name = @name AND o.type IN ('FN','IF','TF');";

        var parameters = new List<SqlParameter>
        {
            new("@schema", schema),
            new("@name", name)
        };

        var record = await _dbContext.SingleAsync<FunctionDefinitionRecord>(sql, parameters, cancellationToken).ConfigureAwait(false);
        var definition = record?.Definition;
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        try
        {
            var extractor = new JsonFunctionAstExtractor();
            var result = extractor.Parse(definition);
            if (!result.ReturnsJson)
            {
                return new FunctionJsonMetadata(false, false, null);
            }

            return new FunctionJsonMetadata(true, result.ReturnsJsonArray, result.JsonRoot);
        }
        catch (Exception ex)
        {
            _console.Verbose($"[fn-json-meta-error] {schema}.{name}: {ex.Message}");
            return null;
        }
    }

    private sealed class FunctionDefinitionRecord
    {
        public string? Definition { get; set; }
    }
}

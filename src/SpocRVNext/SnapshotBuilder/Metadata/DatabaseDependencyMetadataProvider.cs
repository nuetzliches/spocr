using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpocR.SpocRVNext.Data;
using SpocR.SpocRVNext.Services;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

internal sealed class DatabaseDependencyMetadataProvider : IDependencyMetadataProvider
{
    private readonly DbContext _dbContext;
    private readonly IConsoleService _console;

    public DatabaseDependencyMetadataProvider(DbContext dbContext, IConsoleService console)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public async Task<IReadOnlyList<ProcedureDependency>> ResolveAsync(IEnumerable<ProcedureDependency> dependencies, CancellationToken cancellationToken)
    {
        if (dependencies == null) return Array.Empty<ProcedureDependency>();
        var list = dependencies.ToList();
        if (list.Count == 0) return Array.Empty<ProcedureDependency>();

        var results = new List<ProcedureDependency>(list.Count);
        foreach (var dependency in list)
        {
            var resolved = await ResolveAsync(dependency, cancellationToken).ConfigureAwait(false);
            results.Add(resolved ?? dependency);
        }
        return results;
    }

    public async Task<ProcedureDependency?> ResolveAsync(ProcedureDependency dependency, CancellationToken cancellationToken)
    {
        if (dependency == null) return null;
        try
        {
            var lastModified = await GetLastModifiedAsync(dependency, cancellationToken).ConfigureAwait(false);
            if (lastModified == null && dependency.LastModifiedUtc == null)
            {
                return dependency;
            }
            return new ProcedureDependency
            {
                Kind = dependency.Kind,
                Schema = dependency.Schema,
                Name = dependency.Name,
                LastModifiedUtc = lastModified
            };
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-dependency] failed to resolve {dependency}: {ex.Message}");
            return dependency;
        }
    }

    private async Task<DateTime?> GetLastModifiedAsync(ProcedureDependency dependency, CancellationToken cancellationToken)
    {
        if (dependency == null || string.IsNullOrWhiteSpace(dependency.Name))
        {
            return null;
        }

        var schema = dependency.Schema ?? string.Empty;
        switch (dependency.Kind)
        {
            case ProcedureDependencyKind.Procedure:
                return await QueryModifyDateAsync(schema, dependency.Name, new[] { "P" }, cancellationToken).ConfigureAwait(false);
            case ProcedureDependencyKind.Function:
                return await QueryModifyDateAsync(schema, dependency.Name, new[] { "FN", "TF", "IF" }, cancellationToken).ConfigureAwait(false);
            case ProcedureDependencyKind.View:
                return await QueryModifyDateAsync(schema, dependency.Name, new[] { "V" }, cancellationToken).ConfigureAwait(false);
            case ProcedureDependencyKind.Table:
                return await QueryModifyDateAsync(schema, dependency.Name, new[] { "U" }, cancellationToken).ConfigureAwait(false);
            case ProcedureDependencyKind.UserDefinedTableType:
                return await QueryTableTypeModifyDateAsync(schema, dependency.Name, cancellationToken).ConfigureAwait(false);
            case ProcedureDependencyKind.UserDefinedType:
                return await QueryUserDefinedTypeModifyDateAsync(schema, dependency.Name, cancellationToken).ConfigureAwait(false);
            default:
                return null;
        }
    }

    private async Task<DateTime?> QueryModifyDateAsync(string schema, string name, IReadOnlyList<string> objectTypes, CancellationToken cancellationToken)
    {
        if (objectTypes == null || objectTypes.Count == 0) return null;

        var parameters = new List<SqlParameter>
        {
            new("@schema", schema),
            new("@name", name)
        };

        var typeFilter = string.Join(",", objectTypes.Select((_, i) => $"@type{i}"));
        for (var i = 0; i < objectTypes.Count; i++)
        {
            parameters.Add(new SqlParameter($"@type{i}", objectTypes[i]));
        }

        var query = $@"SELECT TOP 1 o.modify_date AS Modified
                        FROM sys.objects AS o
                        INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id
                        WHERE s.name = @schema AND o.name = @name AND o.type IN ({typeFilter})";

        var record = await _dbContext.SingleAsync<ObjectModifyDateRecord>(query, parameters, cancellationToken).ConfigureAwait(false);
        return record?.Modified?.ToUniversalTime();
    }

    private async Task<DateTime?> QueryTableTypeModifyDateAsync(string schema, string name, CancellationToken cancellationToken)
    {
        var parameters = new List<SqlParameter>
        {
            new("@schema", schema),
            new("@name", name)
        };

        const string query = @"SELECT TOP 1 o.modify_date AS Modified
                FROM sys.table_types AS tt
                INNER JOIN sys.schemas AS s ON s.schema_id = tt.schema_id
                INNER JOIN sys.objects AS o ON o.object_id = tt.type_table_object_id
                WHERE s.name = @schema AND tt.name = @name";

        var record = await _dbContext.SingleAsync<ObjectModifyDateRecord>(query, parameters, cancellationToken).ConfigureAwait(false);
        return record?.Modified?.ToUniversalTime();
    }

    private async Task<DateTime?> QueryUserDefinedTypeModifyDateAsync(string schema, string name, CancellationToken cancellationToken)
    {
        var parameters = new List<SqlParameter>
        {
            new("@schema", schema),
            new("@name", name)
        };

        const string query = @"SELECT TOP 1 COALESCE(o.modify_date, o.create_date) AS Modified
                FROM sys.types AS t
                INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                LEFT JOIN sys.objects AS o ON o.object_id = t.user_type_id
                WHERE s.name = @schema AND t.name = @name AND t.is_user_defined = 1 AND t.is_table_type = 0";

        var record = await _dbContext.SingleAsync<ObjectModifyDateRecord>(query, parameters, cancellationToken).ConfigureAwait(false);
        return record?.Modified?.ToUniversalTime();
    }

    private sealed class ObjectModifyDateRecord
    {
        public DateTime? Modified { get; set; }
    }
}

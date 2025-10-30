using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Data;
using SpocR.SpocRVNext.Data.Models;
using SpocR.SpocRVNext.Data.Queries;
using SpocR.SpocRVNext.Services;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

internal sealed class DatabaseTableTypeMetadataProvider : ITableTypeMetadataProvider
{
    private readonly DbContext _dbContext;
    private readonly IConsoleService _console;

    public DatabaseTableTypeMetadataProvider(DbContext dbContext, IConsoleService console)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public async Task<IReadOnlyList<TableTypeMetadata>> GetTableTypesAsync(ISet<string> schemas, CancellationToken cancellationToken)
    {
        if (schemas == null || schemas.Count == 0)
        {
            return Array.Empty<TableTypeMetadata>();
        }

        var escapedSchemas = schemas
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(static s => $"'{s.Replace("'", "''")}'")
            .ToArray();

        if (escapedSchemas.Length == 0)
        {
            return Array.Empty<TableTypeMetadata>();
        }

        var schemaListString = string.Join(',', escapedSchemas);
        List<TableType> tableTypes;
        try
        {
            var list = await _dbContext.TableTypeListAsync(schemaListString, cancellationToken).ConfigureAwait(false);
            tableTypes = list ?? new List<TableType>();
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-tabletype] failed to enumerate table types: {ex.Message}");
            return Array.Empty<TableTypeMetadata>();
        }

        if (tableTypes.Count == 0)
        {
            return Array.Empty<TableTypeMetadata>();
        }

        var results = new List<TableTypeMetadata>(tableTypes.Count);
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
                    var list = await _dbContext.TableTypeColumnListAsync(tableType.UserTypeId.Value, cancellationToken).ConfigureAwait(false);
                    if (list != null)
                    {
                        columns = list;
                    }
                }
                catch (Exception ex)
                {
                    _console.Verbose($"[snapshot-tabletype] failed to load columns for {tableType.SchemaName}.{tableType.Name}: {ex.Message}");
                }
            }

            results.Add(new TableTypeMetadata(tableType, columns));
        }

        return results;
    }
}

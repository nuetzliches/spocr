using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Data;
using SpocR.SpocRVNext.Data.Models;
using SpocR.SpocRVNext.Data.Queries;
using SpocR.Services;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

internal sealed class DatabaseTableMetadataProvider : ITableMetadataProvider
{
    private readonly DbContext _dbContext;
    private readonly IConsoleService _console;

    public DatabaseTableMetadataProvider(DbContext dbContext, IConsoleService console)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public async Task<IReadOnlyList<TableMetadata>> GetTablesAsync(ISet<string> schemas, CancellationToken cancellationToken)
    {
        if (schemas == null || schemas.Count == 0)
        {
            return Array.Empty<TableMetadata>();
        }

        var results = new List<TableMetadata>();

        foreach (var schema in schemas.Where(static s => !string.IsNullOrWhiteSpace(s)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<Table> tables;
            try
            {
                var list = await _dbContext.TableListAsync(schema, cancellationToken).ConfigureAwait(false);
                tables = list ?? new List<Table>();
            }
            catch (Exception ex)
            {
                _console.Verbose($"[snapshot-table] failed to enumerate tables for schema '{schema}': {ex.Message}");
                continue;
            }

            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (table == null || string.IsNullOrWhiteSpace(table.SchemaName) || string.IsNullOrWhiteSpace(table.Name))
                {
                    continue;
                }

                List<Column> columns = new();
                try
                {
                    var list = await _dbContext.TableColumnsListAsync(table.SchemaName, table.Name, cancellationToken).ConfigureAwait(false);
                    if (list != null)
                    {
                        columns = list;
                    }
                }
                catch (Exception ex)
                {
                    _console.Verbose($"[snapshot-table] failed to load columns for {table.SchemaName}.{table.Name}: {ex.Message}");
                }

                results.Add(new TableMetadata(table, columns));
            }
        }

        return results;
    }
}

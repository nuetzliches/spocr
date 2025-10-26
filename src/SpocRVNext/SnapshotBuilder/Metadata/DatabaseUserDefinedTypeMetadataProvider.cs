using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext;
using SpocR.DataContext.Queries;
using SpocR.Services;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

internal sealed class DatabaseUserDefinedTypeMetadataProvider : IUserDefinedTypeMetadataProvider
{
    private readonly DbContext _dbContext;
    private readonly IConsoleService _console;

    public DatabaseUserDefinedTypeMetadataProvider(DbContext dbContext, IConsoleService console)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public async Task<IReadOnlyList<UserDefinedTypeRow>> GetUserDefinedTypesAsync(ISet<string> schemas, CancellationToken cancellationToken)
    {
        try
        {
            var list = await _dbContext.UserDefinedScalarTypesAsync(cancellationToken).ConfigureAwait(false);
            if (list == null || list.Count == 0)
            {
                return Array.Empty<UserDefinedTypeRow>();
            }

            if (schemas == null || schemas.Count == 0)
            {
                return list;
            }

            return list
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.schema_name) && schemas.Contains(row.schema_name))
                .ToList();
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-udt] failed to enumerate user-defined types: {ex.Message}");
            return Array.Empty<UserDefinedTypeRow>();
        }
    }
}

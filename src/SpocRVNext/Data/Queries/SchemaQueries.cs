using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpocR.SpocRVNext.Data.Models;

namespace SpocR.SpocRVNext.Data.Queries;

internal static class SchemaQueries
{
    public static Task<List<Schema>> SchemaListAsync(this DbContext context, CancellationToken cancellationToken)
    {
        const string queryString = "SELECT name FROM sys.schemas WHERE principal_id = 1 ORDER BY name;";
        return context.ListAsync<Schema>(queryString, new List<SqlParameter>(), cancellationToken);
    }
}

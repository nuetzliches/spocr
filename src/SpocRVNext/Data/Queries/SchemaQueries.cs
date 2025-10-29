using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpocR.SpocRVNext.Data.Models;

using DbSchema = SpocR.SpocRVNext.Data.Models.Schema;

namespace SpocR.SpocRVNext.Data.Queries;

internal static class SchemaQueries
{
    public static Task<List<DbSchema>> SchemaListAsync(this DbContext context, CancellationToken cancellationToken)
    {
        const string queryString = "SELECT name FROM sys.schemas WHERE principal_id = 1 ORDER BY name;";
        return context.ListAsync<DbSchema>(queryString, new List<SqlParameter>(), cancellationToken);
    }
}

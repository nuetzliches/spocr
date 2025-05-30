using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Models;

namespace SpocR.DataContext.Queries;

public static class SchemaQueries
{
    public static Task<List<Schema>> SchemaListAsync(this DbContext context, CancellationToken cancellationToken)
    {
        var parameters = new List<SqlParameter>
        {
        };
        var queryString = "SELECT name FROM sys.schemas WHERE principal_id = 1 ORDER BY name;";
        return context.ListAsync<Schema>(queryString, parameters, cancellationToken);
    }
}
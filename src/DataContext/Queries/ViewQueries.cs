using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpocR.DataContext.Models;

namespace SpocR.DataContext.Queries;

public static class ViewQueries
{
    public static async Task<List<Column>> ViewColumnsListAsync(this DbContext context, string schemaName, string viewName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(viewName))
        {
            return new List<Column>();
        }

        var parameters = new List<SqlParameter>
        {
            new("@schemaName", schemaName),
            new("@viewName", viewName)
        };

    var sql = @"SELECT c.name,
               c.is_nullable,
               t.name AS system_type_name,
               IIF(t.name LIKE 'nvarchar%', c.max_length / 2, c.max_length) AS max_length,
               COLUMNPROPERTY(c.object_id, c.name, 'IsIdentity') AS is_identity,
               t1.name AS user_type_name,
               s1.name AS user_type_schema_name,
               t.name AS base_type_name,
               CAST(t.precision AS int) AS precision,
               CAST(t.scale AS int) AS scale
                    FROM sys.views AS v
                    INNER JOIN sys.schemas AS s ON s.schema_id = v.schema_id
                    INNER JOIN sys.columns AS c ON c.object_id = v.object_id
                    INNER JOIN sys.types AS t ON t.system_type_id = c.system_type_id AND t.user_type_id = c.system_type_id
                    LEFT JOIN sys.types AS t1 ON t1.system_type_id = c.system_type_id AND t1.user_type_id = c.user_type_id AND t1.is_user_defined = 1 AND t1.is_table_type = 0
                    LEFT JOIN sys.schemas AS s1 ON s1.schema_id = t1.schema_id
                    WHERE s.name = @schemaName AND v.name = @viewName
                    ORDER BY c.column_id;";

        return await context.ListAsync<Column>(sql, parameters, cancellationToken);
    }
}
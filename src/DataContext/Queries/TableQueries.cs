using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Models;

namespace SpocR.DataContext.Queries;

public static class TableQueries
{
    public static async Task<Column> TableColumnAsync(this DbContext context, string schemaName, string tableName, string columnName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return null;
        }

        var parameters = new List<SqlParameter>
        {
            new("@schemaName", schemaName),
            new("@tableName", tableName),
            new("@columnName", columnName)
        };

        var queryString = @"SELECT c.name,
                                    c.is_nullable,
                                    t.name AS system_type_name,
                                    IIF(t.name LIKE 'nvarchar%', c.max_length / 2, c.max_length) AS max_length
                             FROM sys.tables AS tbl
                             INNER JOIN sys.schemas AS s ON s.schema_id = tbl.schema_id
                             INNER JOIN sys.columns AS c ON c.object_id = tbl.object_id
                             INNER JOIN sys.types AS t ON t.system_type_id = c.system_type_id AND t.user_type_id = c.system_type_id
                             WHERE s.name = @schemaName AND tbl.name = @tableName AND c.name = @columnName;";

        return await context.SingleAsync<Column>(queryString, parameters, cancellationToken);
    }

    public static Task<List<Column>> TableColumnsListAsync(this DbContext context, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(tableName))
        {
            return Task.FromResult(new List<Column>());
        }

        var parameters = new List<SqlParameter>
        {
            new("@schemaName", schemaName),
            new("@tableName", tableName)
        };

        var queryString = @"SELECT c.name,
                    c.is_nullable,
                    t.name AS system_type_name,
                    IIF(t.name LIKE 'nvarchar%', c.max_length / 2, c.max_length) AS max_length,
                    COLUMNPROPERTY(c.object_id, c.name, 'IsIdentity') AS is_identity,
                    t1.name AS user_type_name,
                    s1.name AS user_type_schema_name,
                    t.name AS base_type_name,
                    CAST(c.precision AS int) AS precision,
                    CAST(c.scale AS int) AS scale
                 FROM sys.tables AS tbl
                 INNER JOIN sys.schemas AS s ON s.schema_id = tbl.schema_id
                 INNER JOIN sys.columns AS c ON c.object_id = tbl.object_id
                 INNER JOIN sys.types AS t ON t.system_type_id = c.system_type_id AND t.user_type_id = c.system_type_id
                 LEFT JOIN sys.types AS t1 ON t1.system_type_id = c.system_type_id AND t1.user_type_id = c.user_type_id AND t1.is_user_defined = 1 AND t1.is_table_type = 0
                 LEFT JOIN sys.schemas AS s1 ON s1.schema_id = t1.schema_id
                 WHERE s.name = @schemaName AND tbl.name = @tableName
                 ORDER BY c.column_id;";

        return context.ListAsync<Column>(queryString, parameters, cancellationToken);
    }
}

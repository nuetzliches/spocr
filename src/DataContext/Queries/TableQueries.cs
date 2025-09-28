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
}

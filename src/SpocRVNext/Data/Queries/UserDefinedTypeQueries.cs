using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SpocR.SpocRVNext.Data.Queries;

internal static class UserDefinedTypeQueries
{
    public static Task<List<UserDefinedTypeRow>> UserDefinedScalarTypesAsync(this DbContext context, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT s.name AS schema_name,
                    t1.name AS user_type_name,
                    t.name AS base_type_name,
                    IIF(t.name LIKE 'nvarchar%', t1.max_length / 2, t1.max_length) AS max_length,
                    CAST(t1.precision AS int) AS precision,
                    CAST(t1.scale AS int) AS scale
                             FROM sys.types AS t1
                             INNER JOIN sys.schemas AS s ON s.schema_id = t1.schema_id
                             INNER JOIN sys.types AS t ON t.system_type_id = t1.system_type_id AND t.user_type_id = t1.system_type_id
                             WHERE t1.is_user_defined = 1 AND t1.is_table_type = 0
                             ORDER BY s.name, t1.name;";
        return context.ListAsync<UserDefinedTypeRow>(sql, new List<SqlParameter>(), cancellationToken);
    }
}

internal sealed class UserDefinedTypeRow
{
    public string schema_name { get; set; } = string.Empty;
    public string user_type_name { get; set; } = string.Empty;
    public string base_type_name { get; set; } = string.Empty;
    public int max_length { get; set; }
    public int precision { get; set; }
    public int scale { get; set; }
}

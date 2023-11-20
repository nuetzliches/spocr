using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Models;

namespace SpocR.DataContext.Queries
{
    public static class TableTypeQueries
    {
        public static Task<List<TableType>> TableTypeListAsync(this DbContext context, string schemaList, CancellationToken cancellationToken)
        {
            var parameters = new List<SqlParameter>
            {
            };

            var queryString = @"SELECT tt.user_type_id, tt.name, s.name AS schema_name
                                FROM sys.table_types AS tt
                                    INNER JOIN sys.schemas AS s ON s.schema_id = tt.schema_id 
                                WHERE s.name IN(@schemaList)
                                ORDER BY tt.name ASC;".Replace("@schemaList", schemaList);

            return context.ListAsync<TableType>(queryString, parameters, cancellationToken);
        }

        public static Task<List<Column>> TableTypeColumnListAsync(this DbContext context, int userTypeId, CancellationToken cancellationToken)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@userTypeId", userTypeId)
            };

            // ! the ORDER BY is important
            // max_length see: https://www.sqlservercentral.com/forums/topic/sql-server-max_lenght-returns-double-the-actual-size#unicode
            var queryString = @"SELECT c.name, 
                                    t1.is_nullable, 
                                    t.name AS system_type_name, 
                                    IIF(t.name LIKE 'nvarchar%', c.max_length / 2, c.max_length) AS max_length
                                FROM sys.table_types AS tt
                                INNER JOIN sys.columns c ON c.object_id = tt.type_table_object_id
                                INNER JOIN sys.types t ON t.system_type_id = c.system_type_id AND t.user_type_id = c.system_type_id
                                INNER JOIN sys.types AS t1 ON t1.system_type_id = c.system_type_id AND t1.user_type_id = c.user_type_id  
                                WHERE tt.user_type_id = @userTypeId
                                ORDER BY c.column_id;";

            return context.ListAsync<Column>(queryString, parameters, cancellationToken);
        }
    }
}
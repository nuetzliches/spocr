using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Models;

namespace SpocR.DataContext.Queries
{
    public static class UserTableTypeQueries
    {
        public static Task<List<ColumnDefinition>> UserTableTypeColumnListAsync(this DbContext context, int userTypeId, CancellationToken cancellationToken)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@userTypeId", userTypeId)
            };

            var queryString = @"SELECT c.name, t1.is_nullable, t.name AS system_type_name, c.max_length
                                FROM sys.table_types AS tt
                                INNER JOIN sys.columns c ON c.object_id = tt.type_table_object_id
                                INNER JOIN sys.types t ON t.system_type_id = c.system_type_id AND t.user_type_id = c.system_type_id
                                INNER JOIN sys.types AS t1 ON t1.system_type_id = c.system_type_id AND t1.user_type_id = c.user_type_id  
                                WHERE tt.user_type_id = @userTypeId;";

            return context.ListAsync<ColumnDefinition>(queryString, parameters, cancellationToken);
        }
    }
}
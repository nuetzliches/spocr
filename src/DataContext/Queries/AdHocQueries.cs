using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Models;

namespace SpocR.DataContext.Queries
{
    public static class AdHocQueries
    {
        public static Task<List<StoredProcedureOutput>> AdHocResultSetListAsync(this DbContext context, string query, CancellationToken cancellationToken)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@query", query)
            };
            var queryString = "SELECT name, is_nullable, system_type_name, max_length, is_identity_column FROM sys.dm_exec_describe_first_result_set (@query, NULL, 0) ORDER BY column_ordinal;";
            return context.ListAsync<StoredProcedureOutput>(queryString, parameters, cancellationToken);
        }
    }
}
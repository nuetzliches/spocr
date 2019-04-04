using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.Internal.DataContext.Models;

namespace SpocR.Internal.DataContext.Queries
{
    public static class StoredProcedureQueries
    {
        public static Task<List<StoredProcedure>> StoredProcedureListAsync(this DbContext context, string schemaList, CancellationToken cancellationToken)
        {
            var parameters = new List<SqlParameter>
            {
            };
            var queryString = "SELECT o.schema_id, o.name, o.object_id, o.modify_date FROM sys.objects AS o WHERE o.type = N'P' AND o.schema_id IN(@schemaList);".Replace("@schemaList", schemaList);
            return context.ListAsync<StoredProcedure>(queryString, parameters, cancellationToken);
        }

        public static Task<List<StoredProcedureOutput>> StoredProcedureOutputListAsync(this DbContext context, int objectId, CancellationToken cancellationToken)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@objectId", objectId)
            };
            var queryString = "SELECT name, is_nullable, system_type_name, max_length, is_identity_column FROM sys.dm_exec_describe_first_result_set_for_object (@objectId, 0) ORDER BY column_ordinal;";
            return context.ListAsync<StoredProcedureOutput>(queryString, parameters, cancellationToken);
        }

        public static Task<List<StoredProcedureInput>> StoredProcedureInputListAsync(this DbContext context, int objectId, CancellationToken cancellationToken)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@objectId", objectId)
            };        
            // is_nullable kann beim Input nur über userdefined types definiert werden
            // var queryString = "SELECT p.name, p.is_nullable, t.name AS system_type_name, p.max_length, p.is_output FROM sys.parameters AS p INNER JOIN sys.types t on t.system_type_id = p.system_type_id AND t.user_type_id = p.system_type_id WHERE p.object_id = @objectId ORDER BY p.parameter_id;";
            // var queryString = "SELECT p.name, t1.is_nullable, t.name AS system_type_name, p.max_length, p.is_output FROM sys.parameters AS p INNER JOIN sys.types t on t.system_type_id = p.system_type_id AND t.user_type_id = p.system_type_id INNER JOIN sys.types AS t1 on t1.system_type_id = p.system_type_id AND t1.user_type_id = p.user_type_id WHERE p.object_id = @objectId ORDER BY p.parameter_id;";
            var queryString = @"SELECT p.name, t1.is_nullable, t.name AS system_type_name, p.max_length, p.is_output, t1.is_table_type, t1.name AS user_type_name, t1.user_type_id
                                FROM sys.parameters AS p 
                                LEFT OUTER JOIN sys.types t ON t.system_type_id = p.system_type_id AND t.user_type_id = p.system_type_id 
                                LEFT OUTER JOIN sys.types AS t1 ON t1.system_type_id = p.system_type_id AND t1.user_type_id = p.user_type_id
                                LEFT OUTER JOIN sys.table_types AS tt ON tt.user_type_id = p.user_type_id
                                WHERE p.object_id = @objectId ORDER BY p.parameter_id;";
            return context.ListAsync<StoredProcedureInput>(queryString, parameters, cancellationToken);
        }
    }
}
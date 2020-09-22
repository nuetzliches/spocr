using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Models;

namespace SpocR.DataContext.Queries
{
    public static class StoredProcedureQueries
    {
        public static Task<List<StoredProcedure>> StoredProcedureListAsync(this DbContext context, string schemaList, CancellationToken cancellationToken)
        {
            var parameters = new List<SqlParameter>
            {
            };
            var queryString = "SELECT s.name AS schema_name, o.name, o.modify_date FROM sys.objects AS o INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id WHERE o.type = N'P' AND s.name IN(@schemaList) ORDER BY o.name;".Replace("@schemaList", schemaList);
            return context.ListAsync<StoredProcedure>(queryString, parameters, cancellationToken);
        }

        public static async Task<List<StoredProcedureOutput>> StoredProcedureOutputListAsync(this DbContext context, string schemaName, string name, CancellationToken cancellationToken)
        {
            var storedProcedure = await context.ObjectAsync(schemaName, name, cancellationToken);
            if (storedProcedure == null)
            {
                return null;
            }

            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@objectId", storedProcedure.Id)
            };
            // max_length see: https://www.sqlservercentral.com/forums/topic/sql-server-max_lenght-returns-double-the-actual-size#unicode
            var queryString = @"SELECT name, 
                                    is_nullable, 
                                    system_type_name, 
                                    IIF(system_type_name LIKE 'nvarchar*', max_length / 2, max_length) AS max_length, 
                                    is_identity_column 
                                FROM sys.dm_exec_describe_first_result_set_for_object (@objectId, 0) 
                                ORDER BY column_ordinal;";
            return await context.ListAsync<StoredProcedureOutput>(queryString, parameters, cancellationToken);
        }

        public static async Task<List<StoredProcedureInput>> StoredProcedureInputListAsync(this DbContext context, string schemaName, string name, CancellationToken cancellationToken)
        {
            var storedProcedure = await context.ObjectAsync(schemaName, name, cancellationToken);
            if (storedProcedure == null)
            {
                return null;
            }

            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@objectId", storedProcedure.Id)
            };
            // is_nullable kann beim Input nur über userdefined types definiert werden
            // max_length see: https://www.sqlservercentral.com/forums/topic/sql-server-max_lenght-returns-double-the-actual-size#unicode
            var queryString = @"SELECT p.name, 
                                    t1.is_nullable, 
                                    t.name AS system_type_name, 
                                    IIF(t.name LIKE 'nvarchar*', p.max_length / 2, p.max_length) AS max_length,  
                                    p.is_output, 
                                    t1.is_table_type, 
                                    t1.name AS user_type_name, 
                                    t1.user_type_id
                                FROM sys.parameters AS p 
                                LEFT OUTER JOIN sys.types t ON t.system_type_id = p.system_type_id AND t.user_type_id = p.system_type_id 
                                LEFT OUTER JOIN sys.types AS t1 ON t1.system_type_id = p.system_type_id AND t1.user_type_id = p.user_type_id
                                LEFT OUTER JOIN sys.table_types AS tt ON tt.user_type_id = p.user_type_id
                                WHERE p.object_id = @objectId ORDER BY p.parameter_id;";

            return await context.ListAsync<StoredProcedureInput>(queryString, parameters, cancellationToken);
        }

        public static Task<Object> ObjectAsync(this DbContext context, string schemaName, string name, CancellationToken cancellationToken)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@schemaName", schemaName),
                new SqlParameter("@name", name)
            };
            var queryString = @"SELECT o.object_id
                                FROM sys.objects AS o
                                INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id
                                WHERE s.name = @schemaName AND o.name = @name;";
            return context.SingleAsync<Object>(queryString, parameters, cancellationToken);
        }
    }
}
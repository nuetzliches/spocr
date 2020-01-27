using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Source.DataContext.Models;

namespace Source.DataContext.StoredProcedures.Schema
{
    public static class StoredProcedureExtensions
    {
        public static Task<CrudResult> CrudActionAsync(this IAppDbContext context, int userId, IEnumerable<object> tableType, CancellationToken cancellationToken, AppSqlTransaction transaction = null)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", userId),
                AppDbContext.GetCollectionParameter("TableType", tableType)
            };
            return context.ExecuteSingleAsync<CrudResult>("schema.CrudAction", parameters, cancellationToken, transaction);
        }

        public static Task<CrudResult> CrudActionAsync(this IAppDbContext context, int userId, IEnumerable<object> tableType, IExecuteOptions options, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", userId),
                AppDbContext.GetCollectionParameter("TableType", tableType)
            };
            return context.ExecuteSingleAsync<CrudResult>("schema.CrudAction", parameters, options, cancellationToken);
        }
    }
}
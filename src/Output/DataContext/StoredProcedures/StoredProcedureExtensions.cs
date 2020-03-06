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
        public static Task<CrudResult> CrudActionAsync(this IAppDbContext dbContext, int userId, IEnumerable<object> tableType, CancellationToken cancellationToken, AppSqlTransaction transaction = null)
        {
            if (dbContext == null)
            {
                throw new ArgumentNullException(nameof(dbContext));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", userId),
                AppDbContext.GetCollectionParameter("TableType", tableType)
            };
            return dbContext.ExecuteSingleAsync<CrudResult>("schema.CrudAction", parameters, cancellationToken, transaction);
        }

        public static Task<CrudResult> CrudActionAsync(this IAppDbContext dbContext, int userId, IEnumerable<object> tableType, CancellationToken cancellationToken, IExecuteOptions options = null)
        {
            if (dbContext == null)
            {
                throw new ArgumentNullException(nameof(dbContext));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", userId),
                AppDbContext.GetCollectionParameter("TableType", tableType)
            };
            return dbContext.ExecuteSingleAsync<CrudResult>("schema.CrudAction", parameters, cancellationToken, options);
        }
    }
}
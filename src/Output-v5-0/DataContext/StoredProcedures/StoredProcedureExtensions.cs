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
        [Obsolete("This Method will be removed in next version. Please use next overwrite with model from /Input.")]
        public static Task<CrudResult> CrudActionAsync(this IAppDbContext dbContext, object parameter, IEnumerable<object> tableType, CancellationToken cancellationToken, AppSqlTransaction transaction = null)
        {
            if (dbContext == null)
            {
                throw new ArgumentNullException(nameof(dbContext));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("Parameter", parameter),
                AppDbContext.GetCollectionParameter("TableType", tableType)
            };
            return dbContext.ExecuteSingleAsync<CrudResult>("schema.CrudAction", parameters, new ExecuteOptions
            {
                CancellationToken = cancellationToken,
                Transaction = transaction
            });
        }

        public static Task<CrudResult> CrudActionAsync(this IAppDbContext dbContext, object parameter, IEnumerable<object> tableType, CancellationToken cancellationToken, IExecuteOptions options = null)
        {
            if (dbContext == null)
            {
                throw new ArgumentNullException(nameof(dbContext));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("Parameter", parameter),
                AppDbContext.GetCollectionParameter("TableType", tableType)
            };
            return dbContext.ExecuteSingleAsync<CrudResult>("schema.CrudAction", parameters, new ExecuteOptions
            {
                CancellationToken = cancellationToken,
                Transaction = options?.Transaction
            });
        }
    }
}
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
        public static Task<CrudResult> CrudActionAsync(this IAppDbContextPipe context, Input input, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("Parameter", parameter),
                AppDbContext.GetCollectionParameter("TableType", tableType)
            };
            return context.ExecuteSingleAsync<CrudResult>("schema.CrudAction", parameters, cancellationToken);
        }

        public static Task<CrudResult> CrudActionAsync(this IAppDbContext context, Input input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().CrudActionAsync(input, cancellationToken);
        }
    }
}
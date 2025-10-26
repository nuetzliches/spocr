using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using RestApi.DataContext.Models;
using RestApi.DataContext.Outputs;

namespace RestApi.DataContext.StoredProcedures.Samples
{
    public static class UserOrderHierarchyJsonExtensions
    {
        /// <summary>Executes stored procedure '[samples].[UserOrderHierarchyJson]' and returns the raw JSON string.</summary>
        public static Task<string> UserOrderHierarchyJsonAsync(this IAppDbContextPipe context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
            };
            return context.ReadJsonAsync("[samples].[UserOrderHierarchyJson]", parameters, cancellationToken);
        }

        /// <summary>Executes stored procedure '[samples].[UserOrderHierarchyJson]' and returns the raw JSON string.</summary>
        public static Task<string> UserOrderHierarchyJsonAsync(this IAppDbContext context, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserOrderHierarchyJsonAsync(cancellationToken);
        }
    }
}
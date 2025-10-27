using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using RestApi.DataContext.Models;
using RestApi.DataContext.Outputs;
using RestApi.DataContext.Models.Samples;

namespace RestApi.DataContext.StoredProcedures.Samples
{
    public static class UserOrderHierarchyJsonExtensions
    {
        public static Task<UserOrderHierarchyJson> UserOrderHierarchyJsonAsync(this IAppDbContextPipe context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
            };
            return context.ExecuteSingleAsync<UserOrderHierarchyJson>("[samples].[UserOrderHierarchyJson]", parameters, cancellationToken);
        }

        public static Task<UserOrderHierarchyJson> UserOrderHierarchyJsonAsync(this IAppDbContext context, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserOrderHierarchyJsonAsync(cancellationToken);
        }
    }
}
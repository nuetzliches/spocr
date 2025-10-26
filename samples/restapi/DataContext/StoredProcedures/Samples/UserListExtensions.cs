using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using RestApi.DataContext.Models;
using RestApi.DataContext.Outputs;

namespace RestApi.DataContext.StoredProcedures.Samples
{
    public static class UserListExtensions
    {
        public static Task<Output> UserListAsync(this IAppDbContextPipe context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
            };
            return context.ExecuteAsync<Output>("[samples].[UserList]", parameters, cancellationToken);
        }

        public static Task<Output> UserListAsync(this IAppDbContext context, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserListAsync(cancellationToken);
        }
    }
}
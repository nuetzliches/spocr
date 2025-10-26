using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using RestApi.DataContext.Models;
using RestApi.DataContext.Outputs;
using RestApi.DataContext.Inputs.Samples;

namespace RestApi.DataContext.StoredProcedures.Samples
{
    public static class UserFindExtensions
    {
        public static Task<Output> UserFindAsync(this IAppDbContextPipe context, UserFindInput input, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", input.UserId, false, 4)
            };
            return context.ExecuteAsync<Output>("[samples].[UserFind]", parameters, cancellationToken);
        }

        public static Task<Output> UserFindAsync(this IAppDbContext context, UserFindInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserFindAsync(input, cancellationToken);
        }
    }
}
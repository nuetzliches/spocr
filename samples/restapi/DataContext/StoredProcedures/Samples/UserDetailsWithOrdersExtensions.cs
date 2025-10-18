using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using RestApi.DataContext.Models;
using RestApi.DataContext.Outputs;
using RestApi.DataContext.Models.Samples;
using RestApi.DataContext.Inputs.Samples;

namespace RestApi.DataContext.StoredProcedures.Samples
{
    public static class UserDetailsWithOrdersExtensions
    {
        public static Task<UserDetailsWithOrders> UserDetailsWithOrdersAsync(this IAppDbContextPipe context, UserDetailsWithOrdersInput input, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", input.UserId, false, 4)
            };
            return context.ExecuteSingleAsync<UserDetailsWithOrders>("[samples].[UserDetailsWithOrders]", parameters, cancellationToken);
        }

        public static Task<UserDetailsWithOrders> UserDetailsWithOrdersAsync(this IAppDbContext context, UserDetailsWithOrdersInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserDetailsWithOrdersAsync(input, cancellationToken);
        }
    }
}
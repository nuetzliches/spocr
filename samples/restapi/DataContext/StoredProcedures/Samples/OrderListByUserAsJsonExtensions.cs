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
    public static class OrderListByUserAsJsonExtensions
    {
        public static Task<string> OrderListByUserAsJsonAsync(this IAppDbContextPipe context, OrderListByUserAsJsonInput input, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", input.UserId, false, 4)
            };
            return context.ReadJsonAsync("[samples].[OrderListByUserAsJson]", parameters, cancellationToken);
        }

        public static Task<string> OrderListByUserAsJsonAsync(this IAppDbContext context, OrderListByUserAsJsonInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().OrderListByUserAsJsonAsync(input, cancellationToken);
        }
    }
}
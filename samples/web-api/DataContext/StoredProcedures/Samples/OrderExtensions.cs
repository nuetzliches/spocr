using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using.DataContext.Inputs.Samples;

namespace.DataContext.StoredProcedures.Samples
{
    public static class OrderExtensions
    {
        public static Task<string> OrderListAsJsonAsync(this IAppDbContextPipe context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
            };
            return context.ReadJsonAsync("[samples].[OrderListAsJson]", parameters, cancellationToken);
        }

        public static Task<string> OrderListAsJsonAsync(this IAppDbContext context, CancellationToken cancellationToken)
        {
            return context.CreatePipe().OrderListAsJsonAsync(cancellationToken);
        }

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
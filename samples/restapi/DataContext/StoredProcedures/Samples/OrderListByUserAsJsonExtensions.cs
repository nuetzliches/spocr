using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using RestApi.DataContext.Models;
using RestApi.DataContext.Outputs;
using RestApi.DataContext.Models.Samples;
using RestApi.DataContext.Inputs.Samples;
using System.Text.Json;

namespace RestApi.DataContext.StoredProcedures.Samples
{
    public static class OrderListByUserAsJsonExtensions
    {
        /// <summary>Executes stored procedure '[samples].[OrderListByUserAsJson]' and returns the raw JSON string.</summary>
        /// <remarks>Use <see cref = "OrderListByUserAsJsonDeserializeAsync"/> to obtain a typed model.</remarks>
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

        /// <summary>Executes stored procedure '[samples].[OrderListByUserAsJson]' and returns the raw JSON string.</summary>
        /// <remarks>Use <see cref = "OrderListByUserAsJsonDeserializeAsync"/> to obtain a typed model.</remarks>
        public static Task<string> OrderListByUserAsJsonAsync(this IAppDbContext context, OrderListByUserAsJsonInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().OrderListByUserAsJsonAsync(input, cancellationToken);
        }

        /// <summary>Executes stored procedure '[samples].[OrderListByUserAsJson]' and deserializes the JSON response into OrderListByUserAsJson.</summary>
        /// <remarks>Underlying raw JSON method: <see cref = "OrderListByUserAsJsonAsync"/>.</remarks>
        public static async Task<OrderListByUserAsJson> OrderListByUserAsJsonDeserializeAsync(this IAppDbContextPipe context, OrderListByUserAsJsonInput input, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", input.UserId, false, 4)
            };
            return await context.ReadJsonDeserializeAsync<OrderListByUserAsJson>("[samples].[OrderListByUserAsJson]", parameters, cancellationToken);
        }

        /// <summary>Executes stored procedure '[samples].[OrderListByUserAsJson]' and deserializes the JSON response into OrderListByUserAsJson.</summary>
        /// <remarks>Underlying raw JSON method: <see cref = "OrderListByUserAsJsonAsync"/>.</remarks>
        public static Task<OrderListByUserAsJson> OrderListByUserAsJsonDeserializeAsync(this IAppDbContext context, OrderListByUserAsJsonInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().OrderListByUserAsJsonDeserializeAsync(input, cancellationToken);
        }
    }
}
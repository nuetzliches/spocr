using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using RestApi.DataContext.Models;
using RestApi.DataContext.Outputs;
using RestApi.DataContext.Inputs.Samples;
using RestApi.DataContext.TableTypes.Samples;

namespace RestApi.DataContext.StoredProcedures.Samples
{
    public static class UserContactSyncExtensions
    {
        public static Task<Output> UserContactSyncAsync(this IAppDbContextPipe context, UserContactSyncInput input, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetCollectionParameter("Contacts", input.Contacts)
            };
            return context.ExecuteAsync<Output>("[samples].[UserContactSync]", parameters, cancellationToken);
        }

        public static Task<Output> UserContactSyncAsync(this IAppDbContext context, UserContactSyncInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserContactSyncAsync(input, cancellationToken);
        }
    }
}
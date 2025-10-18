using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using RestApi.DataContext.Models;
using RestApi.DataContext.Outputs;
using RestApi.DataContext.Inputs.Samples;
using RestApi.DataContext.Outputs.Samples;

namespace RestApi.DataContext.StoredProcedures.Samples
{
    public static class CreateUserWithOutputExtensions
    {
        public static Task<int?> CreateUserWithOutputAsync(this IAppDbContextPipe context, CreateUserWithOutputInput input, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("DisplayName", input.DisplayName, false, 128),
                AppDbContext.GetParameter("Email", input.Email, false, 256),
                AppDbContext.GetParameter("UserId", input.UserId, true, 4)
            };
            return context.ExecuteScalarAsync<int?>("[samples].[CreateUserWithOutput]", parameters, cancellationToken);
        }

        public static Task<int?> CreateUserWithOutputAsync(this IAppDbContext context, CreateUserWithOutputInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().CreateUserWithOutputAsync(input, cancellationToken);
        }
    }
}
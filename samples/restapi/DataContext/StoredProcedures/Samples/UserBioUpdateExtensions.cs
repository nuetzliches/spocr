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
    public static class UserBioUpdateExtensions
    {
        public static Task<UserBioUpdate> UserBioUpdateAsync(this IAppDbContextPipe context, UserBioUpdateInput input, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", input.UserId, false, 4),
                AppDbContext.GetParameter("Bio", input.Bio, false, 512)
            };
            return context.ExecuteSingleAsync<UserBioUpdate>("[samples].[UserBioUpdate]", parameters, cancellationToken);
        }

        public static Task<UserBioUpdate> UserBioUpdateAsync(this IAppDbContext context, UserBioUpdateInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserBioUpdateAsync(input, cancellationToken);
        }
    }
}
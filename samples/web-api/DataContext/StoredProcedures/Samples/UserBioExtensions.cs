using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using.DataContext.Models;
using.DataContext.Outputs;
using.DataContext.Inputs.Samples;

namespace.DataContext.StoredProcedures.Samples
{
    public static class UserBioExtensions
    {
        public static Task<CrudResult> UserBioUpdateAsync(this IAppDbContextPipe context, UserBioUpdateInput input, CancellationToken cancellationToken)
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
            return context.ExecuteSingleAsync<CrudResult>("[samples].[UserBioUpdate]", parameters, cancellationToken);
        }

        public static Task<CrudResult> UserBioUpdateAsync(this IAppDbContext context, UserBioUpdateInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserBioUpdateAsync(input, cancellationToken);
        }
    }
}
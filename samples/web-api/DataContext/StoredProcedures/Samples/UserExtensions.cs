using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using.DataContext.Models.Samples;
using.DataContext.Inputs.Samples;

namespace.DataContext.StoredProcedures.Samples
{
    public static class UserExtensions
    {
        public static Task<UserFind> UserFindAsync(this IAppDbContextPipe context, UserFindInput input, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", input.UserId, false, 4)
            };
            return context.ExecuteSingleAsync<UserFind>("[samples].[UserFind]", parameters, cancellationToken);
        }

        public static Task<UserFind> UserFindAsync(this IAppDbContext context, UserFindInput input, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserFindAsync(input, cancellationToken);
        }

        public static Task<List<UserList>> UserListAsync(this IAppDbContextPipe context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
            };
            return context.ExecuteListAsync<UserList>("[samples].[UserList]", parameters, cancellationToken);
        }

        public static Task<List<UserList>> UserListAsync(this IAppDbContext context, CancellationToken cancellationToken)
        {
            return context.CreatePipe().UserListAsync(cancellationToken);
        }
    }
}
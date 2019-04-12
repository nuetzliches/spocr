/*  Warning! This file was auto-generated from @[Name].
    Please don't hurt me, no more! 

    Inherit me, to override my behaviour - if you'd like ;)

    @@[Name] 
    Version: @[Version]
*/

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Source.DataContext.Models;

namespace Source.DataContext.StoredProcedures.Schema
{
    public static class StoredProcedureExtensions
    {
        public static Task<CrudResult> CrudActionAsync(this IAppDbContext context, int userId, CancellationToken cancellationToken, AppSqlTransaction transaction = null)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var parameters = new List<SqlParameter>
            {
                AppDbContext.GetParameter("UserId", userId)
            };
            return context.ExecuteSingleAsync<CrudResult>("schema.CrudAction", parameters, cancellationToken, transaction);
        }
        
    }
}
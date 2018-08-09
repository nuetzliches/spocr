﻿/*  Warning! This file is generated from @[Name].
    Please read the documentation if changes are required.

    @@[Name] 
    Version: @[Version]
    LastModified: @[LastModified]
    Locked: @[Locked]
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
        public static Task<CrudResult> CrudActionAsync(this AppDbContext context, int userId, CancellationToken cancellationToken, AppDbContext.AppSqlTransaction transaction = null)
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
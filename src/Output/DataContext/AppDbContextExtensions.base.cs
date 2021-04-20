using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Source.DataContext
{
    public static class AppDbContextExtensions
    {
        public static IAppDbContextPipe WithCommandTimeout(this IAppDbContext context, int commandTimeout)
        {
            return context.CreatePipe().WithCommandTimeout(commandTimeout);
        }

        public static IAppDbContextPipe CreatePipe(this IAppDbContext context)
        {
            return new AppDbContextPipe(context);
        }
    }

    public static class AppDbContextPipeExtensions
    {
        public static IAppDbContextPipe WithCommandTimeout(this IAppDbContextPipe pipe, int commandTimeout)
        {
            pipe.CommandTimeout = commandTimeout;
            return pipe;
        }

        public static async Task ExecuteAsync(this IAppDbContextPipe pipe, string procedureName, List<SqlParameter> parameters, CancellationToken cancellationToken = default)
        {
            var command = await pipe.CreateSqlCommandAsync(procedureName, parameters, cancellationToken);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task<List<T>> ExecuteListAsync<T>(this IAppDbContextPipe pipe, string procedureName, List<SqlParameter> parameters, CancellationToken cancellationToken = default) where T : class, new()
        {
            var command = await pipe.CreateSqlCommandAsync(procedureName, parameters, cancellationToken);

            var result = new List<T>();

            var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(reader.ConvertToObject<T>());
            reader.Close();

            return result;
        }

        public static async Task<T> ExecuteSingleAsync<T>(this IAppDbContextPipe pipe, string procedureName, List<SqlParameter> parameters, CancellationToken cancellationToken = default) where T : class, new()
        {
            return (await pipe.ExecuteListAsync<T>(procedureName, parameters, cancellationToken)).SingleOrDefault();
        }

        public static async Task<string> ReadJsonAsync(this IAppDbContextPipe pipe, string procedureName, List<SqlParameter> parameters, CancellationToken cancellationToken = default)
        {
            var command = await pipe.CreateSqlCommandAsync(procedureName, parameters, cancellationToken);

            var result = new StringBuilder();
            var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (reader.Read()) result.Append(reader.GetValue(0).ToString());
            reader.Close();

            return result.ToString();
        }

        internal static async Task<SqlCommand> CreateSqlCommandAsync(this IAppDbContextPipe pipe, string procedureName, List<SqlParameter> parameters, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pipe.Context.Connection.State != ConnectionState.Open) await pipe.Context.Connection.OpenAsync(cancellationToken);

            var command = new SqlCommand(procedureName, pipe.Context.Connection)
            {
                CommandType = CommandType.StoredProcedure,
                Transaction = pipe.GetCurrentTransaction()
            };

            if (pipe.CommandTimeout.HasValue)
            {
                command.CommandTimeout = pipe.CommandTimeout.Value;
            }

            if (parameters?.Any() ?? false) command.Parameters.AddRange(parameters.ToArray());

            return command;
        }

        internal static SqlTransaction GetCurrentTransaction(this IAppDbContextPipe pipe)
        {
            return pipe.Transaction ?? pipe.Context.Transactions.LastOrDefault();
        }
    }
}
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Internal.DataContext;

namespace SpocR.Internal.DataContext
{
    public class DbContext : IDisposable
    {
        private SqlConnection _connection;

        private readonly List<AppSqlTransaction> _transactions = new List<AppSqlTransaction>();

        public DbContext(string connectionString = null)
        {
            SetConnectionString(connectionString);
        }

        public DbContext(SqlConnection connection)
        {
            _connection = connection;
        }

        public void SetConnectionString(string connectionString)
        {
            _connection = new SqlConnection(connectionString);
        }

        public void Dispose()
        {
            if (_connection?.State == ConnectionState.Open)
            {
                if (_transactions.Any()) _transactions.ForEach(t => t.Transaction.Rollback());
                _connection.Close();
            }

            _connection?.Dispose();
        }

        public async Task<List<T>> ExecuteListAsync<T>(string procedureName, List<SqlParameter> parameters,
            CancellationToken cancellationToken = default(CancellationToken), AppSqlTransaction transaction = null) where T : class, new()
        {
            cancellationToken.ThrowIfCancellationRequested();

            // NOTE: https://stackoverflow.com/questions/4439409/open-close-sqlconnection-or-keep-open/4439434#4439434
            if (_connection.State != ConnectionState.Open) await _connection.OpenAsync(cancellationToken);

            var command = new SqlCommand(procedureName, _connection)
            {
                CommandType = CommandType.StoredProcedure,
                Transaction = transaction?.Transaction
            };

            if (parameters?.Any() ?? false) command.Parameters.AddRange(parameters.ToArray());

            var result = new List<T>();

            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (reader.Read()) result.Add(reader.ConvertToObject<T>());
            }

            if (!_transactions.Any()) _connection.Close();

            return result;
        }

        public async Task<T> ExecuteSingleAsync<T>(string procedureName, List<SqlParameter> parameters,
            CancellationToken cancellationToken = default(CancellationToken), AppSqlTransaction transaction = null) where T : class, new()
        {
            cancellationToken.ThrowIfCancellationRequested();

            return (await ExecuteListAsync<T>(procedureName, parameters, cancellationToken, transaction)).SingleOrDefault();
        }

        public async Task<List<T>> ListAsync<T>(string queryString, List<SqlParameter> parameters,
            CancellationToken cancellationToken = default(CancellationToken), AppSqlTransaction transaction = null) where T : class, new()
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_connection.State != ConnectionState.Open) await _connection.OpenAsync(cancellationToken);

            var command = new SqlCommand(queryString, _connection)
            {
                CommandType = CommandType.Text,
                Transaction = transaction?.Transaction
            };

            if (parameters?.Any() ?? false) command.Parameters.AddRange(parameters.ToArray());

            var result = new List<T>();

            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (reader.Read()) result.Add(reader.ConvertToObject<T>());
            }

            if (!_transactions.Any()) _connection.Close();

            return result;
        }

        public async Task<T> SingleAsync<T>(string queryString, List<SqlParameter> parameters,
            CancellationToken cancellationToken = default(CancellationToken), AppSqlTransaction transaction = null) where T : class, new()
        {
            cancellationToken.ThrowIfCancellationRequested();

            return (await ListAsync<T>(queryString, parameters, cancellationToken, transaction)).SingleOrDefault();
        }

        public async Task<AppSqlTransaction> BeginTransactionAsync(string transactionName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_connection.State != ConnectionState.Open) await _connection.OpenAsync(cancellationToken);
            var transaction = new AppSqlTransaction { Transaction = _connection.BeginTransaction(transactionName) };
            _transactions.Add(transaction);
            return transaction;
        }

        public void CommitTransaction(AppSqlTransaction transaction)
        {
            var trans = _transactions.SingleOrDefault(t => t.Equals(transaction));
            if (trans == null) return;
            trans.Transaction.Commit();
            _transactions.Remove(trans);
        }

        public void RollbackTransaction(AppSqlTransaction transaction)
        {
            var trans = _transactions.SingleOrDefault(t => t.Equals(transaction));
            if (trans == null) return;
            trans.Transaction.Rollback();
            _transactions.Remove(trans);
        }

        public static SqlParameter GetParameter(string parameter, object value)
        {
            return new SqlParameter(parameter, value ?? DBNull.Value)
            {
                Direction = ParameterDirection.Input,
                SqlDbType = GetSqlDbType(value)
            };
        }

        public static SqlDbType GetSqlDbType(object value)
        {
            switch (value)
            {
                case int _:
                    return SqlDbType.Int;
                case long _:
                    return SqlDbType.BigInt;
                case string _:
                    return SqlDbType.NVarChar;
                case bool _:
                    return SqlDbType.Bit;
                case DateTime _:
                    return SqlDbType.DateTime2;
                case Guid _:
                    return SqlDbType.UniqueIdentifier;
                case decimal _:
                    return SqlDbType.Decimal;
                case null:
                    return SqlDbType.NVarChar;
                default:
                    // NOTE: https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings
                    throw new ArgumentOutOfRangeException($"{nameof(DbContext)}.{nameof(GetSqlDbType)} - System.Type {value.GetType()} not defined!");
            }
        }

        public class AppSqlTransaction
        {
            public SqlTransaction Transaction { get; set; }
        }
    }

    public static class DbContextServiceCollectionExtensions
    {
        public static IServiceCollection AddDbContext(this IServiceCollection services, string connectionString = null)
        {
            services.AddSingleton(new DbContext(connectionString));
            return services;
        }
    }

    // public interface IDbContextBuilder {
    //     string ConnectionString {get;set;}
    // }
}
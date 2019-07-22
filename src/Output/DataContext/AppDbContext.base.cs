using Microsoft.Extensions.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Source.DataContext
{
    public interface IAppDbContext
    {
        Task<List<T>> ExecuteListAsync<T>(string procedureName, List<SqlParameter> parameters, CancellationToken cancellationToken, AppSqlTransaction transaction = null) where T : class, new();
        Task<T> ExecuteSingleAsync<T>(string procedureName, List<SqlParameter> parameters, CancellationToken cancellationToken, AppSqlTransaction transaction = null) where T : class, new();
        Task<string> ReadJsonAsync(string procedureName, List<SqlParameter> parameters,
            CancellationToken cancellationToken = default, AppSqlTransaction transaction = null);
        Task<AppSqlTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
        Task<AppSqlTransaction> BeginTransactionAsync(string transactionName, CancellationToken cancellationToken);
        Task<AppSqlTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken);
        Task<AppSqlTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, string transactionName, CancellationToken cancellationToken);
        void CommitTransaction(AppSqlTransaction transaction);
        void RollbackTransaction(AppSqlTransaction transaction);
        void Dispose();
    }

    public class AppDbContext : IAppDbContext, IDisposable
    {
        private readonly SqlConnection _connection;
        private readonly List<AppSqlTransaction> _transactions;

        public AppDbContext(IConfiguration configuration)
        {
            _connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
            _transactions = new List<AppSqlTransaction>();
        }

        public void Dispose()
        {
            if (_connection?.State == ConnectionState.Open)
            {
                if (_transactions.Any()) 
                    // We need a copy - Rollback will modify this List
                    _transactions.ToList().ForEach(RollbackTransaction);
                _connection.Close();
            }

            _connection?.Dispose();
        }

        public async Task<List<T>> ExecuteListAsync<T>(string procedureName, List<SqlParameter> parameters,
            CancellationToken cancellationToken = default, AppSqlTransaction transaction = null) where T : class, new()
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_connection.State != ConnectionState.Open) await _connection.OpenAsync(cancellationToken);

            var command = new SqlCommand(procedureName, _connection)
            {
                CommandType = CommandType.StoredProcedure,
                Transaction = transaction?.Transaction
            };

            if (parameters?.Any() ?? false) command.Parameters.AddRange(parameters.ToArray());

            var result = new List<T>();

            var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(reader.ConvertToObject<T>());
            reader.Close();

            return result;
        }

        public Task<AppSqlTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return BeginTransactionAsync(null, cancellationToken);
        }

        public Task<AppSqlTransaction> BeginTransactionAsync(string transactionName, CancellationToken cancellationToken = default)
        {
            return BeginTransactionAsync(IsolationLevel.Unspecified, transactionName, cancellationToken);
        }

        public Task<AppSqlTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
        {
            return BeginTransactionAsync(isolationLevel, null, cancellationToken);
        }

        public async Task<AppSqlTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, string transactionName, CancellationToken cancellationToken = default)
        {
            if (_connection.State != ConnectionState.Open) await _connection.OpenAsync(cancellationToken);
            var transaction = new AppSqlTransaction
            {
                Transaction = !string.IsNullOrEmpty(transactionName)
                        ? _connection.BeginTransaction(isolationLevel, transactionName)
                        : _connection.BeginTransaction(isolationLevel)
            };
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

        public async Task<T> ExecuteSingleAsync<T>(string procedureName, List<SqlParameter> parameters,
            CancellationToken cancellationToken = default, AppSqlTransaction transaction = null) where T : class, new()
        {
            cancellationToken.ThrowIfCancellationRequested();

            return (await ExecuteListAsync<T>(procedureName, parameters, cancellationToken, transaction)).SingleOrDefault();
        }

        public async Task<string> ReadJsonAsync(string procedureName, List<SqlParameter> parameters,
            CancellationToken cancellationToken = default, AppSqlTransaction transaction = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_connection.State != ConnectionState.Open) await _connection.OpenAsync(cancellationToken);

            var command = new SqlCommand(procedureName, _connection)
            {
                CommandType = CommandType.StoredProcedure,
                Transaction = transaction?.Transaction
            };

            if (parameters?.Any() ?? false) command.Parameters.AddRange(parameters.ToArray());
            
            var result = new StringBuilder();

            var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (reader.Read()) result.Append(reader.GetValue(0).ToString());
            reader.Close();

            return result.ToString();
        }

        public static SqlParameter GetParameter(string parameter, object value)
        {
            var valueType = value?.GetType();
            var isCollection = typeof(IEnumerable<>).IsAssignableFrom(valueType);
            if (isCollection)
            {
                return new SqlParameter(parameter, value?.ToSqlParamCollection())
                {
                    Direction = ParameterDirection.Input,
                    SqlDbType = SqlDbType.Structured
                };
            }

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
                case double _:
                    return SqlDbType.Float;
                case byte[] _:
                    return SqlDbType.VarBinary;
                case null:
                    return SqlDbType.NVarChar;
                default:
                    // NOTE: https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings
                    throw new NotImplementedException($"{nameof(AppDbContext)}.{nameof(GetSqlDbType)} - System.Type {value.GetType()} not defined!");
            }
        }
    }

    public class AppSqlTransaction
    {
        public SqlTransaction Transaction { get; set; }
    }
}
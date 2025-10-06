using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace Source.DataContext
{

    public interface IAppDbContextPipe
    {
        IAppDbContext Context { get; }
        SqlTransaction Transaction { get; set; }
        int? CommandTimeout { get; set; }
    }

    public interface IAppDbContext
    {
        AppDbContextOptions Options { get; }
        SqlConnection Connection { get; }
        List<SqlTransaction> Transactions { get; }
        Task<SqlTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
        Task<SqlTransaction> BeginTransactionAsync(string transactionName, CancellationToken cancellationToken);
        Task<SqlTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken);
        Task<SqlTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, string transactionName, CancellationToken cancellationToken);
        void CommitTransaction(SqlTransaction transaction);
        void RollbackTransaction(SqlTransaction transaction);
        void Dispose();
    }

    public class AppDbContextOptions
    {
        /// <summary>
        /// The CommandTimeout in Seconds
        /// </summary>
        public int CommandTimeout { get; set; } = 30;
        /// <summary>
        /// Optional JsonSerializerOptions used by ReadJsonDeserializeAsync. If null a default (PropertyNameCaseInsensitive=true) is used.
        /// </summary>
        public JsonSerializerOptions JsonSerializerOptions { get; set; }
    }

    public class AppDbContextPipe : IAppDbContextPipe
    {
        public AppDbContextPipe(IAppDbContext context)
        {
            Context = context;
        }

        public IAppDbContext Context { get; }
        public SqlTransaction Transaction { get; set; }
        public int? CommandTimeout { get; set; }
    }

    public class AppDbContext : IAppDbContext, IDisposable
    {
        public AppDbContextOptions Options { get; }
        public SqlConnection Connection { get; }
        public List<SqlTransaction> Transactions { get; }

        public AppDbContext(IConfiguration configuration, IOptions<AppDbContextOptions> options)
        {
            Options = options.Value;
            Connection = new SqlConnection(configuration.GetConnectionString("<spocr>DefaultConnection</spocr>"));
            Transactions = new List<SqlTransaction>();
        }

        public void Dispose()
        {
            if (Connection?.State == ConnectionState.Open)
            {
                if (Transactions.Any())
                    // We need a copy - Rollback will modify this List
                    Transactions.ToList().ForEach(RollbackTransaction);
                Connection.Close();
            }

            Connection?.Dispose();
        }

        public Task<SqlTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return BeginTransactionAsync(null, cancellationToken);
        }

        public Task<SqlTransaction> BeginTransactionAsync(string transactionName, CancellationToken cancellationToken = default)
        {
            return BeginTransactionAsync(IsolationLevel.Unspecified, transactionName, cancellationToken);
        }

        public Task<SqlTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
        {
            return BeginTransactionAsync(isolationLevel, null, cancellationToken);
        }

        public async Task<SqlTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, string transactionName, CancellationToken cancellationToken = default)
        {
            if (Connection.State != ConnectionState.Open) await Connection.OpenAsync(cancellationToken);
            var transaction = !string.IsNullOrEmpty(transactionName)
                        ? Connection.BeginTransaction(isolationLevel, transactionName)
                        : Connection.BeginTransaction(isolationLevel);
            Transactions.Add(transaction);
            return transaction;
        }

        public void CommitTransaction(SqlTransaction transaction)
        {
            var trans = Transactions.SingleOrDefault(t => t.Equals(transaction));
            if (trans == null) return;
            trans.Commit();
            Transactions.Remove(trans);
        }

        public void RollbackTransaction(SqlTransaction transaction)
        {
            var trans = Transactions.SingleOrDefault(t => t.Equals(transaction));
            if (trans == null) return;
            trans.Rollback();
            Transactions.Remove(trans);
        }

        public static SqlParameter GetParameter<T>(string parameter, T value, bool output = false, int? size = null)
        {
            var input = value as object;

            // handle DateTimes
            var type = typeof(T);
            if (value != null && (type == typeof(DateTime) || type == typeof(DateTime?)))
            {
                var useUtc = parameter.EndsWith("Utc", StringComparison.InvariantCultureIgnoreCase);
                if (useUtc)
                {
                    input = (value as DateTime?)?.ToUniversalTime();
                }
                else
                {
                    input = (value as DateTime?)?.ToLocalTime();
                }
            }

            // NVARCHAR(MAX) parameters are not handled correctly in some drivers. Workaround:
            if (size == null && type == typeof(string)) size = 1070000000;

            return new SqlParameter(parameter, input ?? DBNull.Value)
            {
                Direction = output ? ParameterDirection.Output : ParameterDirection.Input,
                SqlDbType = GetSqlDbType(typeof(T)),
                Size = size ?? 0
            };
        }

        public static SqlParameter GetCollectionParameter<T>(string parameter, T value)
        {
            return new SqlParameter(parameter, value?.ToSqlParamCollection())
            {
                Direction = ParameterDirection.Input,
                SqlDbType = SqlDbType.Structured
            };
        }

        public static SqlDbType GetSqlDbType(Type type)
        {
            switch (type)
            {
                case Type t when t == typeof(int) || t == typeof(int?):
                    return SqlDbType.Int;
                case Type t when t == typeof(long) || t == typeof(long?):
                    return SqlDbType.BigInt;
                case Type t when t == typeof(string):
                    return SqlDbType.NVarChar;
                case Type t when t == typeof(bool) || t == typeof(bool?):
                    return SqlDbType.Bit;
                case Type t when t == typeof(DateTime) || t == typeof(DateTime?):
                    return SqlDbType.DateTime2;
                case Type t when t == typeof(Guid) || t == typeof(Guid?):
                    return SqlDbType.UniqueIdentifier;
                case Type t when t == typeof(decimal) || t == typeof(decimal?):
                    return SqlDbType.Decimal;
                case Type t when t == typeof(double) || t == typeof(double?):
                    return SqlDbType.Float;
                case Type t when t == typeof(byte[]) || t == typeof(byte?[]):
                    return SqlDbType.VarBinary;
                default:
                    // NOTE: https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings
                    throw new NotImplementedException($"{nameof(AppDbContext)}.{nameof(GetSqlDbType)} - System.Type {type} not defined!");
            }
        }
    }
}

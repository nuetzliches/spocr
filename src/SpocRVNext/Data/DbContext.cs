using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext.Services;

namespace SpocR.SpocRVNext.Data;

public class DbContext(IConsoleService consoleService) : IDisposable
{
    private SqlConnection? _connection;
    private List<AppSqlTransaction>? _transactions;

    public void SetConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must not be null or whitespace.", nameof(connectionString));
        }

        if (_transactions?.Count > 0)
        {
            foreach (var transaction in _transactions.ToArray())
            {
                try
                {
                    RollbackTransaction(transaction);
                }
                catch (Exception ex)
                {
                    consoleService.Verbose($"[dbctx] rollback during reconfigure failed: {ex.Message}");
                }
            }
        }

        if (_connection != null)
        {
            try
            {
                if (_connection.State != ConnectionState.Closed)
                {
                    _connection.Close();
                }
            }
            catch (Exception ex)
            {
                consoleService.Verbose($"[dbctx] close during reconfigure failed: {ex.Message}");
            }
            finally
            {
                _connection.Dispose();
            }
        }

        _transactions = [];
        _connection = new SqlConnection(connectionString);
    }

    public void Dispose()
    {
        if (_connection?.State == ConnectionState.Open)
        {
            if (_transactions?.Any() == true)
            {
                _transactions.ToList().ForEach(RollbackTransaction);
            }

            _connection.Close();
        }

        _connection?.Dispose();
    }

    public async Task<List<T>> ExecuteListAsync<T>(string procedureName, List<SqlParameter> parameters, CancellationToken cancellationToken = default, AppSqlTransaction? transaction = null) where T : class, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<T>();

        try
        {
            if (_connection == null)
            {
                throw new InvalidOperationException("Connection string not configured.");
            }

            if (_connection.State != ConnectionState.Open)
            {
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            using var command = new SqlCommand(procedureName, _connection)
            {
                CommandType = CommandType.StoredProcedure,
                Transaction = transaction?.Transaction ?? GetCurrentTransaction()?.Transaction
            };

            if (parameters?.Any() == true)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(reader.ConvertToObject<T>());
            }

            if ((_transactions?.Any() ?? false) == false)
            {
                _connection.Close();
            }
        }
        catch (Exception ex)
        {
            consoleService.Error($"Error in ExecuteListAsync for {procedureName}: {ex.Message}");
            throw;
        }

        return result;
    }

    public async Task<T?> ExecuteSingleAsync<T>(string procedureName, List<SqlParameter> parameters, CancellationToken cancellationToken = default, AppSqlTransaction? transaction = null) where T : class, new()
    {
        var list = await ExecuteListAsync<T>(procedureName, parameters, cancellationToken, transaction).ConfigureAwait(false);
        return list.SingleOrDefault();
    }

    public async Task<List<T>> ListAsync<T>(string queryString, List<SqlParameter> parameters, CancellationToken cancellationToken = default, AppSqlTransaction? transaction = null) where T : class, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<T>();

        var intercepted = await OnListAsync<T>(queryString, parameters, cancellationToken, transaction).ConfigureAwait(false);
        if (intercepted != null)
        {
            return intercepted;
        }

        try
        {
            if (_connection == null)
            {
                throw new InvalidOperationException("Connection string not configured.");
            }

            if (_connection.State != ConnectionState.Open)
            {
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            using var command = new SqlCommand(queryString, _connection)
            {
                CommandType = CommandType.Text,
                Transaction = transaction?.Transaction ?? GetCurrentTransaction()?.Transaction
            };

            if (parameters?.Any() == true)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(reader.ConvertToObject<T>());
            }

            if ((_transactions?.Any() ?? false) == false)
            {
                _connection.Close();
            }
        }
        catch (Exception ex)
        {
            consoleService.Error($"Error in ListAsync for query: {ex.Message}");
            throw;
        }

        return result;
    }

    protected virtual Task<List<T>?> OnListAsync<T>(string queryString, List<SqlParameter> parameters, CancellationToken cancellationToken, AppSqlTransaction? transaction) where T : class, new()
    {
        return Task.FromResult<List<T>?>(null);
    }

    public async Task<T?> SingleAsync<T>(string queryString, List<SqlParameter> parameters, CancellationToken cancellationToken = default, AppSqlTransaction? transaction = null) where T : class, new()
    {
        var list = await ListAsync<T>(queryString, parameters, cancellationToken, transaction).ConfigureAwait(false);
        return list.SingleOrDefault();
    }

    public async Task<AppSqlTransaction> BeginTransactionAsync(string transactionName, CancellationToken cancellationToken = default)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("Connection string not configured.");
        }

        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var transaction = new AppSqlTransaction { Transaction = _connection.BeginTransaction(transactionName) };
        _transactions ??= [];
        _transactions.Add(transaction);
        return transaction;
    }

    public void CommitTransaction(AppSqlTransaction transaction)
    {
        if (_transactions == null)
        {
            return;
        }

        var existing = _transactions.SingleOrDefault(t => t.Equals(transaction));
        if (existing == null)
        {
            return;
        }

        existing.Transaction.Commit();
        _transactions.Remove(existing);
    }

    public void RollbackTransaction(AppSqlTransaction transaction)
    {
        if (_transactions == null)
        {
            return;
        }

        var existing = _transactions.SingleOrDefault(t => t.Equals(transaction));
        if (existing == null)
        {
            return;
        }

        existing.Transaction.Rollback();
        _transactions.Remove(existing);
    }

    public AppSqlTransaction? GetCurrentTransaction()
    {
        return _transactions?.LastOrDefault();
    }

    public static SqlParameter GetParameter(string parameter, object? value)
    {
        return new SqlParameter(parameter, value ?? DBNull.Value)
        {
            Direction = ParameterDirection.Input,
            SqlDbType = GetSqlDbType(value)
        };
    }

    public static SqlDbType GetSqlDbType(object? value) => value switch
    {
        int => SqlDbType.Int,
        long => SqlDbType.BigInt,
        string => SqlDbType.NVarChar,
        bool => SqlDbType.Bit,
        DateTime => SqlDbType.DateTime2,
        Guid => SqlDbType.UniqueIdentifier,
        decimal => SqlDbType.Decimal,
        double => SqlDbType.Float,
        byte[] => SqlDbType.VarBinary,
        null => SqlDbType.NVarChar,
        _ => throw new ArgumentOutOfRangeException($"{nameof(DbContext)}.{nameof(GetSqlDbType)} - System.Type {value.GetType()} not defined!")
    };

    public sealed class AppSqlTransaction
    {
        public SqlTransaction Transaction { get; set; } = null!;
    }
}

internal static class DbContextServiceCollectionExtensions
{
    public static IServiceCollection AddDbContext(this IServiceCollection services)
    {
        services.AddSingleton(provider => new DbContext(provider.GetRequiredService<IConsoleService>()));
        return services;
    }
}

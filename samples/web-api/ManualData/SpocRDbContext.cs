// -----------------------------------------------------------------------------------------------------------------
// This file is a MANUAL sample DbContext used solely for the WebApi sample while the modern generated pipeline
// (AppDbContext / IAppDbContextPipe etc.) is still being evolved. It is NOT part of the generated SpocR output.
// Once the modern context + execution abstractions are finalized this folder can be removed.
// -----------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace SpocR.Samples.WebApi.ManualData;

public interface ISpocRDbContext : IAsyncDisposable
{
    SpocRDbContextOptions Options { get; }
    Task<ISpocRTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default);
    Task<int> ExecuteNonQueryAsync(string storedProcedure, object? parameters = null, ISpocRTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<T?> ExecuteScalarAsync<T>(string storedProcedure, object? parameters = null, ISpocRTransaction? transaction = null, CancellationToken cancellationToken = default);
}

public interface ISpocRTransaction : IAsyncDisposable
{
    SqlTransaction Inner { get; }
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public sealed class SpocRDbContextOptions
{
    public string? ConnectionString { get; set; }
    public int CommandTimeout { get; set; } = 30;
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new() { PropertyNameCaseInsensitive = true };
}

internal sealed class SpocRTransaction : ISpocRTransaction
{
    private bool _completed;
    public SqlTransaction Inner { get; }
    public SpocRTransaction(SqlTransaction inner) => Inner = inner;
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) return; await Inner.CommitAsync(cancellationToken); _completed = true;
    }
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) return; try { await Inner.RollbackAsync(cancellationToken); } catch { } _completed = true;
    }
    public async ValueTask DisposeAsync()
    {
        if (!_completed) await RollbackAsync();
        await Inner.DisposeAsync();
    }
}

public sealed class SpocRDbContext : ISpocRDbContext
{
    private readonly ILogger<SpocRDbContext> _logger;
    private readonly SqlConnection _connection;
    private bool _disposed;
    public SpocRDbContextOptions Options { get; }

    public SpocRDbContext(SpocRDbContextOptions options, ILogger<SpocRDbContext> logger)
    {
        Options = options;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("SpocRDbContextOptions.ConnectionString not configured");
        _connection = new SqlConnection(options.ConnectionString);
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(ct);
    }

    public async Task<ISpocRTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        await EnsureOpenAsync(cancellationToken);
        var tx = (SqlTransaction)await _connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        return new SpocRTransaction(tx);
    }

    public async Task<int> ExecuteNonQueryAsync(string storedProcedure, object? parameters = null, ISpocRTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        using var cmd = await PrepareCommandAsync(storedProcedure, parameters, transaction, cancellationToken);
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<T?> ExecuteScalarAsync<T>(string storedProcedure, object? parameters = null, ISpocRTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        using var cmd = await PrepareCommandAsync(storedProcedure, parameters, transaction, cancellationToken);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result == null || result is DBNull) return default;
        return (T)Convert.ChangeType(result, typeof(T));
    }

    private async Task<SqlCommand> PrepareCommandAsync(string storedProcedure, object? parameters, ISpocRTransaction? transaction, CancellationToken ct)
    {
        await EnsureOpenAsync(ct);
        var isRawSql = storedProcedure.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || storedProcedure.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
        var cmd = _connection.CreateCommand();
        cmd.CommandText = storedProcedure;
        cmd.CommandType = isRawSql ? CommandType.Text : CommandType.StoredProcedure;
        cmd.CommandTimeout = Options.CommandTimeout;
        if (transaction?.Inner != null) cmd.Transaction = transaction.Inner;
        if (parameters != null)
        {
            foreach (var prop in parameters.GetType().GetProperties())
            {
                var value = prop.GetValue(parameters) ?? DBNull.Value;
                cmd.Parameters.Add(new SqlParameter("@" + prop.Name, value));
            }
        }
        return cmd;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_connection.State == ConnectionState.Open)
            await _connection.CloseAsync();
        await _connection.DisposeAsync();
        _disposed = true;
    }
}

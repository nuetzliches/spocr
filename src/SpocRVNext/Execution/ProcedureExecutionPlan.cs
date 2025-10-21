using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SpocR.SpocRVNext.Execution;

/// <summary>
/// Immutable description of how to execute a stored procedure and materialize its results.
/// </summary>
public sealed class ProcedureExecutionPlan
{
    // Fully qualified stored procedure name (schema + name). Generator now supplies an already bracketed, escaped identifier.
    public string ProcedureName { get; }
    public IReadOnlyList<ProcedureParameter> Parameters { get; }
    public IReadOnlyList<ResultSetMapping> ResultSets { get; }
    public Func<IReadOnlyDictionary<string, object?>, object?>? OutputFactory { get; }
    public Action<DbCommand, object?>? InputBinder { get; }
    public Func<bool, string?, object?, IReadOnlyDictionary<string, object?>, object[], object> AggregateFactory { get; }

    public ProcedureExecutionPlan(
        string procedureName,
        IReadOnlyList<ProcedureParameter> parameters,
        IReadOnlyList<ResultSetMapping> resultSets,
        Func<IReadOnlyDictionary<string, object?>, object?>? outputFactory,
        Func<bool, string?, object?, IReadOnlyDictionary<string, object?>, object[], object> aggregateFactory,
        Action<DbCommand, object?>? inputBinder = null)
    {
        // Name is assumed pre-bracketed by code generator (no further normalization here to avoid double quoting / hidden mutations).
        ProcedureName = procedureName;
        Parameters = parameters;
        ResultSets = resultSets;
        OutputFactory = outputFactory;
        AggregateFactory = aggregateFactory;
        InputBinder = inputBinder;
    }
}

public sealed record ProcedureParameter(string Name, DbType? DbType, int? Size, bool IsOutput, bool IsNullable);

public sealed record ResultSetMapping(string Name, Func<DbDataReader, CancellationToken, Task<IReadOnlyList<object>>> Materializer);

public static class ProcedureExecutor
{
    private static ISpocRProcedureInterceptor _interceptor = new NoOpProcedureInterceptor();

    /// <summary>Sets a global interceptor. Thread-safe overwrite; expected rarely (e.g. application startup).</summary>
    public static void SetInterceptor(ISpocRProcedureInterceptor interceptor) => _interceptor = interceptor ?? new NoOpProcedureInterceptor();

    public static async Task<TAggregate> ExecuteAsync<TAggregate>(DbConnection connection, ProcedureExecutionPlan plan, object? state = null, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = plan.ProcedureName;
        cmd.CommandType = CommandType.StoredProcedure;
        foreach (var p in plan.Parameters)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = p.Name;
            if (p.DbType.HasValue) param.DbType = p.DbType.Value;
            if (p.Size.HasValue) param.Size = p.Size.Value;
            param.Direction = p.IsOutput ? ParameterDirection.InputOutput : ParameterDirection.Input;
            if (!p.IsOutput)
            {
                // Value binding is deferred to generated wrapper; here default to DBNull (overridden by wrapper before execute);
                param.Value = DBNull.Value;
            }
            cmd.Parameters.Add(param);
        }

        object? beforeState = null;
        var start = DateTime.UtcNow;
        try
        {
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            // Bind input values (if any) before execution
            plan.InputBinder?.Invoke(cmd, state); // wrapper supplies state (input record) if available

            beforeState = await _interceptor.OnBeforeExecuteAsync(plan.ProcedureName, cmd, state, cancellationToken).ConfigureAwait(false);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var resultSetResults = new List<object>(plan.ResultSets.Count);
            for (int i = 0; i < plan.ResultSets.Count; i++)
            {
                var map = plan.ResultSets[i];
                var list = await map.Materializer(reader, cancellationToken).ConfigureAwait(false);
                resultSetResults.Add(list);
                if (i < plan.ResultSets.Count - 1)
                {
                    if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)) break;
                }
            }
            // Collect output parameter values
            var outputValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in plan.Parameters)
            {
                if (p.IsOutput)
                {
                    var val = cmd.Parameters[p.Name].Value;
                    outputValues[p.Name.TrimStart('@')] = val == DBNull.Value ? null : val;
                }
            }
            object? outputObj = plan.OutputFactory?.Invoke(outputValues);
            var rsArray = resultSetResults.ToArray();
            var aggregateObj = plan.AggregateFactory(true, null, outputObj, outputValues, rsArray);
            var duration = DateTime.UtcNow - start;
            await _interceptor.OnAfterExecuteAsync(plan.ProcedureName, cmd, true, null, duration, beforeState, aggregateObj, cancellationToken).ConfigureAwait(false);
            return (TAggregate)aggregateObj;
        }
        catch (Exception ex)
        {
            var aggregateObj = plan.AggregateFactory(false, ex.Message, null, new Dictionary<string, object?>(), Array.Empty<object>());
            var duration = DateTime.UtcNow - start;
            try { await _interceptor.OnAfterExecuteAsync(plan.ProcedureName, cmd, false, ex.Message, duration, beforeState, aggregateObj, cancellationToken).ConfigureAwait(false); } catch { /* swallow interceptor errors */ }
            return (TAggregate)aggregateObj;
        }
    }
}
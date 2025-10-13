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
    public string ProcedureName { get; }
    public IReadOnlyList<ProcedureParameter> Parameters { get; }
    public IReadOnlyList<ResultSetMapping> ResultSets { get; }
    public Func<IReadOnlyDictionary<string, object?>, object?>? OutputFactory { get; }
    public Func<bool, string?, object?, IReadOnlyDictionary<string, object?>, object[], object> AggregateFactory { get; }

    public ProcedureExecutionPlan(
        string procedureName,
        IReadOnlyList<ProcedureParameter> parameters,
        IReadOnlyList<ResultSetMapping> resultSets,
        Func<IReadOnlyDictionary<string, object?>, object?>? outputFactory,
        Func<bool, string?, object?, IReadOnlyDictionary<string, object?>, object[], object> aggregateFactory)
    {
        ProcedureName = procedureName;
        Parameters = parameters;
        ResultSets = resultSets;
        OutputFactory = outputFactory;
        AggregateFactory = aggregateFactory;
    }
}

public sealed record ProcedureParameter(string Name, DbType? DbType, int? Size, bool IsOutput, bool IsNullable);

public sealed record ResultSetMapping(string Name, Func<DbDataReader, CancellationToken, Task<IReadOnlyList<object>>> Materializer);

public static class ProcedureExecutor
{
    public static async Task<TAggregate> ExecuteAsync<TAggregate>(DbConnection connection, ProcedureExecutionPlan plan, CancellationToken cancellationToken = default)
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
        try
        {
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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
            var aggregate = plan.AggregateFactory(true, null, outputObj, outputValues, rsArray);
            return (TAggregate)aggregate;
        }
        catch (Exception ex)
        {
            var aggregate = plan.AggregateFactory(false, ex.Message, null, new Dictionary<string, object?>(), Array.Empty<object>());
            return (TAggregate)aggregate;
        }
    }
}
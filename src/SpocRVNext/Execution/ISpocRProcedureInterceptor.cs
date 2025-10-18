using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SpocR.SpocRVNext.Execution;

/// <summary>
/// Interceptor hook points for procedure execution. Implementations can log, trace, or mutate parameters before execution.
/// Lifecycle: OnBeforeExecute (timing start) -> ProcedureExecutor core -> OnAfterExecute (success/failure + duration).
/// </summary>
public interface ISpocRProcedureInterceptor
{
    /// <summary>Called before the command is executed. Return a state object carried into <see cref="OnAfterExecute"/>.</summary>
    Task<object?> OnBeforeExecuteAsync(string procedureName, DbCommand command, object? state, CancellationToken cancellationToken);
    /// <summary>Called after execution completes (success or failure). Receives state from <see cref="OnBeforeExecuteAsync"/>.</summary>
    Task OnAfterExecuteAsync(string procedureName, DbCommand command, bool success, string? error, TimeSpan duration, object? beforeState, object? aggregate, CancellationToken cancellationToken);
}

/// <summary>
/// Default no-op interceptor used when none registered.
/// </summary>
internal sealed class NoOpProcedureInterceptor : ISpocRProcedureInterceptor
{
    public Task<object?> OnBeforeExecuteAsync(string procedureName, DbCommand command, object? state, CancellationToken cancellationToken) => Task.FromResult<object?>(null);
    public Task OnAfterExecuteAsync(string procedureName, DbCommand command, bool success, string? error, TimeSpan duration, object? beforeState, object? aggregate, CancellationToken cancellationToken) => Task.CompletedTask;
}

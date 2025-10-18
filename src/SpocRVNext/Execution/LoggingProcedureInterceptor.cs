using System;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SpocR.SpocRVNext.Execution;

/// <summary>
/// Logging interceptor capturing duration, success flag and optional error. Designed for lightweight structured logging without throwing.
/// Register via <c>ProcedureExecutor.SetInterceptor(new LoggingProcedureInterceptor(logger));</c> during application startup.
/// </summary>
public sealed class LoggingProcedureInterceptor : ISpocRProcedureInterceptor
{
    private readonly ILogger _logger;

    public LoggingProcedureInterceptor(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<object?> OnBeforeExecuteAsync(string procedureName, DbCommand command, object? state, CancellationToken cancellationToken)
    {
        // Capture high-resolution timestamp for duration fallback (executor also measures but we keep our own for completeness)
        return Task.FromResult<object?>(Stopwatch.GetTimestamp());
    }

    public Task OnAfterExecuteAsync(string procedureName, DbCommand command, bool success, string? error, TimeSpan duration, object? beforeState, object? aggregate, CancellationToken cancellationToken)
    {
        try
        {
            var paramCount = command.Parameters.Count;
            // Avoid enumerating potentially large result sets; log only metadata
            if (success)
            {
                _logger.LogInformation("spocr.proc.executed {Procedure} duration_ms={DurationMs} params={ParamCount} success={Success}", procedureName, duration.TotalMilliseconds, paramCount, true);
            }
            else
            {
                _logger.LogWarning("spocr.proc.failed {Procedure} duration_ms={DurationMs} params={ParamCount} success={Success} error={Error}", procedureName, duration.TotalMilliseconds, paramCount, false, error);
            }
        }
        catch
        {
            // Swallow logging exceptions to not affect execution path
        }
        return Task.CompletedTask;
    }
}

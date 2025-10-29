using SpocR.Enums;
using SpocR.Infrastructure;

namespace SpocR.Commands;

public static class CommandResultMapper
{
    public static int Map(ExecuteResultEnum result) => result switch
    {
        ExecuteResultEnum.Succeeded => ExitCodes.Success,
        ExecuteResultEnum.Aborted => ExitCodes.ValidationError,
        ExecuteResultEnum.Error => ExitCodes.GenerationError,
        ExecuteResultEnum.Skipped => ExitCodes.Success,
        ExecuteResultEnum.Exception => ExitCodes.InternalError,
        _ => ExitCodes.InternalError
    };
}

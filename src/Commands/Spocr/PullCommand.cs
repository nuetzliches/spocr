using McMaster.Extensions.CommandLineUtils;
using SpocR.Runtime;
using System.Threading.Tasks;
using SpocR.Enums;
using SpocR.Infrastructure;

namespace SpocR.Commands.Spocr;

[HelpOption("-?|-h|--help")]
[Command(
    "pull",
    Description = "Pull database metadata into .spocr snapshots using .env settings",
    ExtendedHelpText = "Requires SPOCR_GENERATOR_DB from .env (seed via 'spocr init'). JSON helpers ship enabled by default.")]
public class PullCommand(
    SpocrCliRuntime cliRuntime
) : SpocrCommandBase
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        var result = await cliRuntime.PullAsync(CommandOptions);
        return Map(result);
    }

    // kept for backward compatibility but prefer CommandResultMapper.Map
    internal static int Map(ExecuteResultEnum result) => CommandResultMapper.Map(result);
}

internal static class CommandResultMapper
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

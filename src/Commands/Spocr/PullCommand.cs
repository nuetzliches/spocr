using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using System.Threading.Tasks;
using SpocR.Enums;
using SpocR.Infrastructure;

namespace SpocR.Commands.Spocr;

[HelpOption("-?|-h|--help")]
[Command("pull", Description = "Pull all schema informations from DB into spocr.json")]
public class PullCommand(
    SpocrManager spocrManager,
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager)
{
    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();
        var result = await spocrManager.PullAsync(CommandOptions);
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

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SpocR.Attributes;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SpocrConfigManager(
    IConsoleService consoleService,
    FileManager<GlobalConfigurationModel> globalConfigFile
)
{
    public async Task<ExecuteResultEnum> ConfigAsync()
    {
        if (!await globalConfigFile.ExistsAsync())
        {
            consoleService.Error($"Global config is missing!");
        }

        var config = await globalConfigFile.ReadAsync();

        var propertyInfos = typeof(GlobalConfigurationModel)
                                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                .Where(prop => !(prop.GetCustomAttribute<WriteProtectedBySystem>()?.IsProtected ?? false));

        consoleService.Warn("Please enter your Configuration:");

        foreach (var prop in propertyInfos)
        {
            var input = consoleService.GetString(prop.Name, prop.GetValue(config)?.ToString());
            prop.SetValue(config, input);
        }

        var proceed = consoleService.GetYesNo("Write your entries to GlobalConfigFile?", true, ConsoleColor.Red);
        if (!proceed) return ExecuteResultEnum.Aborted;

        await globalConfigFile.SaveAsync(config);

        return ExecuteResultEnum.Succeeded;
    }
}

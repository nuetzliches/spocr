using System;
using System.Linq;
using System.Reflection;
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
    public EExecuteResult Config()
    {
        if (!globalConfigFile.Exists())
        {
            consoleService.Error($"Global config is missing!");
        }

        var config = globalConfigFile.Read();

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
        if (!proceed) return EExecuteResult.Aborted;

        globalConfigFile.Save(config);

        return EExecuteResult.Succeeded;
    }
}

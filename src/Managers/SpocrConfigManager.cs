using System;
using System.Linq;
using System.Reflection;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Attributes;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SpocrConfigManager(
    IReportService reportService,
    FileManager<GlobalConfigurationModel> globalConfigFile
)
{
    public ExecuteResultEnum Config()
    {
        if (!globalConfigFile.Exists())
        {
            reportService.Error($"Global config is missing!");
        }

        var config = globalConfigFile.Read();

        var propertyInfos = typeof(GlobalConfigurationModel)
                                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                .Where(prop => !(prop.GetCustomAttribute<WriteProtectedBySystem>()?.IsProtected ?? false));

        reportService.Warn("Please enter your Configuration:");

        foreach (var prop in propertyInfos)
        {
            var input = Prompt.GetString(prop.Name, prop.GetValue(config)?.ToString());
            prop.SetValue(config, input);
        }

        var proceed = Prompt.GetYesNo("Write your entries to GlobalConfigFile?", true, ConsoleColor.Red);
        if (!proceed) return ExecuteResultEnum.Aborted;

        globalConfigFile.Save(config);

        return ExecuteResultEnum.Succeeded;
    }
}

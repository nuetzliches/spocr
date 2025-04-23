using System;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Models;

namespace SpocR.Extensions;

public static class CommandLineApplicationExtensions
{
    public static CommandLineApplication InitializeGlobalConfig<T>(this CommandLineApplication<T> cli, IServiceProvider serviceProvider) where T : class
    {
        var globalConfigFile = serviceProvider.GetService<FileManager<GlobalConfigurationModel>>();
        // var version = Assembly.GetEntryAssembly().GetName().Version;

        if (!globalConfigFile.Exists())
        {
            globalConfigFile.Save(globalConfigFile.DefaultConfig);
        }
        // else 
        // {
        // TODO handle existing File with lower Version as current version
        // this allows migrations
        // }

        return cli;
    }
}

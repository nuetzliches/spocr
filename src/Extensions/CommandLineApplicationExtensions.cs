using System;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Models;
using System.Threading.Tasks;

namespace SpocR.Extensions;

public static class CommandLineApplicationExtensions
{
    public static async Task<CommandLineApplication> InitializeGlobalConfigAsync<T>(this CommandLineApplication<T> cli, IServiceProvider serviceProvider) where T : class
    {
        var globalConfigFile = serviceProvider.GetService<FileManager<GlobalConfigurationModel>>();
        // var version = Assembly.GetEntryAssembly().GetName().Version;

        if (!globalConfigFile.Exists())
        {
            await globalConfigFile.SaveAsync(globalConfigFile.DefaultConfig);
        }
        // else 
        // {
        // TODO handle existing File with lower Version as current version
        // this allows migrations
        // }

        return cli;
    }
}

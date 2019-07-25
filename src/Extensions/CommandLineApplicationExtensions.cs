using System;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Models;
using System.Reflection;

namespace SpocR.Extensions 
{
    public static class CommandLineApplicationExtensions
    {
        public static CommandLineApplication InitializeGlobalConfig<T>(this CommandLineApplication<T> cli,  IServiceProvider serviceProvider) where T : class
        { 
            var globalConfigFile = serviceProvider.GetService<FileManager<GlobalConfigurationModel>>();
            var version = Assembly.GetEntryAssembly().GetName().Version;

            if(!globalConfigFile.Exists()) 
            {
                var config = new GlobalConfigurationModel {
                    Version = version
                };

                globalConfigFile.Save(config);
            } 
            // else 
            // {
                // TODO handle existing File with lower Version as current version
                // this allows migrations
            // }
        
            return cli;
        }
    }

}
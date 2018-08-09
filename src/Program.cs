using System;
using System.ComponentModel.DataAnnotations;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Internal.Common;
using SpocR.Internal.DataContext;
using SpocR.Internal.Managers;

namespace SpocR
{
    class Program
    {
        static int Main(string[] args)
        {
            var serviceProvider = new ServiceCollection()
                .AddSpocR()
                .AddDbContext()
                .AddCommandLineInterface()
                .AddSingleton<IReporter>(new ConsoleReporter(PhysicalConsole.Singleton))
                .AddSingleton<SchemaManager>()
                .BuildServiceProvider();

            return serviceProvider
                .GetRequiredService<CommandLineInterface>()
                .Configure(serviceProvider)
                .Execute(args);
        }
    }
}


using System;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Commands;
using SpocR.DataContext;
using SpocR.Extensions;
using SpocR.Utils;

namespace SpocR
{
    [Command(ThrowOnUnexpectedArgument = false)]
    [Subcommand("create", typeof(CreateCommand))]
    [Subcommand("pull", typeof(PullCommand))]
    [Subcommand("build", typeof(BuildCommand))]
    [Subcommand("rebuild", typeof(RebuildCommand))]
    [Subcommand("remove", typeof(RemoveCommand))]
    [Subcommand("version", typeof(VersionCommand))]
    [HelpOption("-?|-h|--help")]
    public class Program
    {
        static void Main(string[] args)
        {
            var aspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            var builder = new ConfigurationBuilder()
                .SetBasePath(DirectoryUtils.GetApplicationRoot())
                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile($"appsettings.{aspNetCoreEnvironment}.json", true, true);

            var configuration = builder.Build();

            var serviceProvider = new ServiceCollection()
                .AddSpocR()
                .AddDbContext()
                .AddSingleton<IReporter>(new ConsoleReporter(PhysicalConsole.Singleton))
                .AddSingleton<IConfiguration>(configuration)
                .BuildServiceProvider();

            var app = new CommandLineApplication<Program>
            {
                Name = "spocr",
                Description = ".NET Core console for SpocR"
            };

            app.Conventions
               .UseDefaultConventions()
               .UseConstructorInjection(serviceProvider);

            app.OnExecute(() =>
            {
                app.ShowRootCommandFullNameAndVersion();
                app.ShowHelp();
                return 0;
            });

            app.Execute(args);
        }
    }
}

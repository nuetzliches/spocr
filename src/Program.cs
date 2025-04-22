using System;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Commands.Project;
using SpocR.Commands.Schema;
using SpocR.Commands.Spocr;
using SpocR.Commands.StoredProcdure;
using SpocR.DataContext;
using SpocR.Extensions;
using SpocR.Utils;

namespace SpocR;

[Command(UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw)]
[Subcommand(typeof(CreateCommand))]
[Subcommand(typeof(PullCommand))]
[Subcommand(typeof(BuildCommand))]
[Subcommand(typeof(RebuildCommand))]
[Subcommand(typeof(RemoveCommand))]
[Subcommand(typeof(VersionCommand))]
[Subcommand(typeof(ConfigCommand))]
[Subcommand(typeof(ProjectCommand))]
[Subcommand(typeof(SchemaCommand))]
[Subcommand(typeof(StoredProcdureCommand))]
[HelpOption("-?|-h|--help")]
public class Program
{
    static async Task Main(string[] args)
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

        app.InitializeGlobalConfig(serviceProvider);

        app.OnExecute(() =>
        {
            app.ShowRootCommandFullNameAndVersion();
            app.ShowHelp();
            return 0;
        });

        await Task.Run(() => app.Execute(args));
    }
}

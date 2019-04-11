using System;
using System.ComponentModel.DataAnnotations;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Commands;
using SpocR.Internal.Common;
using SpocR.Internal.DataContext;
using SpocR.Internal.Managers;
using SpocR.Managers;

namespace SpocR
{
    [Command(ThrowOnUnexpectedArgument = false)]
    [Subcommand("create", typeof(CreateCommand))]
    [Subcommand("pull", typeof(PullCommand))]
    [Subcommand("build", typeof(BuildCommand))]
    [Subcommand("rebuild", typeof(RebuildCommand))]
    [Subcommand("remove", typeof(RemoveCommand))]
    [HelpOption("-?|-h|--help")]
    public class Program
    {
        static void Main(string[] args)
        {
            var serviceProvider = new ServiceCollection()
                .AddSpocR()
                .AddDbContext()
                .AddSingleton<IReporter>(new ConsoleReporter(PhysicalConsole.Singleton))
                .AddSingleton<SchemaManager>()
                .AddSingleton<SpocrManager>()
                .BuildServiceProvider();

            var dbContext = serviceProvider.GetService<DbContext>();
            var engine = serviceProvider.GetService<Engine>();
            
            if (!string.IsNullOrWhiteSpace(engine.Config?.Project?.DataBase?.ConnectionString))
            {
                dbContext.SetConnectionString(engine.Config.Project.DataBase.ConnectionString);
            }

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

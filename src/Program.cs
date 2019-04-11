
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Commands;
using SpocR.Extensions;
using SpocR.Internal.DataContext;
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
                .BuildServiceProvider();

            var dbContext = serviceProvider.GetService<DbContext>();
            var configFile = serviceProvider.GetService<ConfigFileManager>();
            
            if (!string.IsNullOrWhiteSpace(configFile.Config?.Project?.DataBase?.ConnectionString))
            {
                dbContext.SetConnectionString(configFile.Config.Project.DataBase.ConnectionString);
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

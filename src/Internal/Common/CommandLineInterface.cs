using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Internal.DataContext;
using SpocR.Internal.Managers;
using SpocR.Internal.Models;

namespace SpocR.Internal.Common
{
    internal class CommandLineInterface : CommandLineApplication
    {
        internal IServiceProvider ServiceProvider { get; private set; }

        public CommandLineInterface()
        {
            Name = Configuration.Name;
            Description = Configuration.Description;
        }

        internal CommandLineInterface Configure(IServiceProvider serviceProvider, Action<CommandLineInterface> config = null)
        {
            ServiceProvider = serviceProvider;
            if (config == null)
            {
                return CommandLineInterfaceConfiguration.Configure(this);
            }
            config.Invoke(this);
            return this;
        }

        public new int Execute(params string[] args)
        {
            if (ServiceProvider == null)
            {
                throw new InvalidOperationException($"{ServiceProvider} is null. Did you run {nameof(Configure)}(<IServiceProvider>) before?");
            }
            return base.Execute(args);
        }
    }

    internal static class CommandLineInterfaceConfiguration
    {
        public static CommandLineInterface Configure(CommandLineInterface cli)
        {
            var engine = cli.ServiceProvider.GetService<Engine>();
            var reporter = cli.ServiceProvider.GetService<IReporter>();
            var dbContext = cli.ServiceProvider.GetService<DbContext>();
            var shemaManager = cli.ServiceProvider.GetService<SchemaManager>();

            if (!string.IsNullOrWhiteSpace(engine.Config?.Project?.DataBase?.ConnectionString))
            {
                dbContext.SetConnectionString(engine.Config.Project.DataBase.ConnectionString);
            }

            #region Configuration

            cli.HelpOption(inherited: true);
            cli.Command("create", createCmd =>
            {
                var dryrunOption = createCmd.Option("-d|--dry-run", "Run test without any changes", CommandOptionType.NoValue);
                createCmd.OnExecute(() =>
                {
                    var dryrun = dryrunOption.HasValue();
                    OnCreate(cli, dryrun);
                });

            });

            cli.Command("pull", pullCmd =>
            {
                var dryrunOption = pullCmd.Option("-d|--dry-run", "Run test without any changes", CommandOptionType.NoValue);
                pullCmd.OnExecute(() =>
                {
                    var dryrun = dryrunOption.HasValue();
                    OnPull(cli, dryrun);
                });
            });

            cli.Command("build", buildCmd =>
            {
                var dryrunOption = buildCmd.Option("-d|--dry-run", "Run test without any changes", CommandOptionType.NoValue);
                buildCmd.OnExecute(() =>
                {
                    var dryrun = dryrunOption.HasValue();
                    OnBuild(cli, dryrun);
                });
            });

            cli.Command("rebuild", rebuildCmd =>
            {
                var dryrunOption = rebuildCmd.Option("-d|--dry-run", "Run test without any changes", CommandOptionType.NoValue);
                rebuildCmd.OnExecute(() =>
                {
                    var dryrun = dryrunOption.HasValue();
                    if (OnPull(cli, dryrun) == 1)
                        OnBuild(cli, dryrun);
                });
            });

            cli.Command("remove", removeCmd =>
            {
                var dryrunOption = removeCmd.Option("-d|--dry-run", "Run test without any changes", CommandOptionType.NoValue);
                removeCmd.OnExecute(() =>
                {
                    var dryrun = dryrunOption.HasValue();
                    OnRemove(cli, dryrun);
                });
            });

            // cli.Command("status", statusCmd =>
            // {
            //     statusCmd.OnExecute(() =>
            //     {
            //         throw new NotImplementedException();
            //         // TODO: Display current Status/Configuration
            //         // Version, Last Modified, etc...
            //         // Maybe extended information about created project structure

            //         return (int)ExecuteResultEnum.Succeeded;
            //     });
            // });

            cli.OnExecute(() =>
            {
                cli.ShowHelp();
                return (int)ExecuteResultEnum.Succeeded;
            });

            // cli.Command("config", configCmd =>
            // {
            //     configCmd.OnExecute(() =>
            //     {
            //         Console.WriteLine("Specify a subcommand");
            //         configCmd.ShowHelp();
            //         return 1;
            //     });

            //     configCmd.Command("set", setCmd =>
            //     {
            //         setCmd.Description = "Set config value";
            //         var key = setCmd.Argument("key", "Name of the config").IsRequired();
            //         var val = setCmd.Argument("value", "Value of the config").IsRequired();
            //         setCmd.OnExecute(() =>
            //         {
            //             Console.WriteLine($"Setting config {key.Value} = {val.Value}");
            //         });
            //     });

            //     configCmd.Command("list", listCmd =>
            //     {
            //         var json = listCmd.Option("--json", "Json output", CommandOptionType.NoValue);
            //         listCmd.OnExecute(() =>
            //         {
            //             if (json.HasValue())
            //             {
            //                 Console.WriteLine("{\"dummy\": \"value\"}");
            //             }
            //             else
            //             {
            //                 Console.WriteLine("dummy = value");
            //             }
            //         });
            //     });
            // });

            #endregion

            return cli;
        }

        private static int OnCreate(CommandLineInterface cli, bool dryrun)
        {
            var engine = cli.ServiceProvider.GetService<Engine>();
            var reporter = cli.ServiceProvider.GetService<IReporter>();

            if (engine.ConfigFileExists())
            {
                reporter.Error($"File already exists: {Configuration.ConfigurationFile}");
                reporter.Output($"\tPlease run: {Configuration.Name} status");
                return (int)ExecuteResultEnum.Error;
            }

            if(dryrun) 
            {
                reporter.Output($"Create as dry run.");
            }

            var proceed = Prompt.GetYesNo("Create a new SpocR Project?", true);
            if (!proceed) return (int)ExecuteResultEnum.Aborted;

            var appNamespace = Prompt.GetString("Your Project Namespace:", new DirectoryInfo(cli.WorkingDirectory).Name);
            var connectionString = Prompt.GetString("Your ConnectionString:");

            var roleKindString = Prompt.GetString("SpocR Role [Default, Lib, Extension]:", "Default");
            var roleKind = default(ERoleKind);
            Enum.TryParse(roleKindString, true, out roleKind);

            var role = new RoleModel
            {
                Kind = roleKind,
                LibNamespace = roleKind == ERoleKind.Extension
                    ? Prompt.GetString("SpocR Lib Namespace:", "Nuts.DbContext")
                    : null
            };

            var config = new ConfigurationModel
            {
                Version = Configuration.Version,
                Modified = DateTime.Now,
                Project = new ProjectModel
                {
                    Namespace = appNamespace,
                    Role = role,
                    DataBase = new DataBaseModel
                    {
                        RuntimeConnectionStringIdentifier = "DefaultConnection",
                        ConnectionString = connectionString ?? ""
                    },
                    Structure = engine.GetStructureModelListFromSource()
                },
                Schema = new List<SchemaModel>()
            };

            engine.SaveConfigFile(config);
            reporter.Output($"{Configuration.Name} successfully created.");

            return (int)ExecuteResultEnum.Succeeded;
        }

        private static int OnPull(CommandLineInterface cli, bool dryrun)
        {
            var engine = cli.ServiceProvider.GetService<Engine>();
            var reporter = cli.ServiceProvider.GetService<IReporter>();
            var shemaManager = cli.ServiceProvider.GetService<SchemaManager>();

            if(dryrun) 
            {
                reporter.Output($"Pull as dry run.");
            }

            if (!engine.ConfigFileExists())
            {
                reporter.Error($"File not found: {Configuration.ConfigurationFile}");
                reporter.Output($"\tPlease make sure you are in the right working directory");
                return (int)ExecuteResultEnum.Error;
            }

            if (string.IsNullOrWhiteSpace(engine.Config.Project.DataBase.ConnectionString))
            {
                reporter.Error($"ConnectionString is empty: {Configuration.ConfigurationFile}");
                reporter.Output($"\tPlease run {Configuration.Name} set --cs <ConnectionString>");
                return (int)ExecuteResultEnum.Error;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            shemaManager.ListAsync(true, engine.Config).ContinueWith(t =>
            {
                var result = t.Result;
                // overwrite with current config
                if (engine.Config?.Schema != null)
                {
                    foreach (var schema in result)
                    {
                        var currentSchema = engine.Config.Schema.FirstOrDefault(i => i.Id == schema.Id);
                        schema.Status = currentSchema != null ? currentSchema.Status : SchemaStatusEnum.Build;
                    }
                }
                engine.Config.Schema = result;

            }).Wait();

            var spCount = engine.Config.Schema.SelectMany(x => x.StoredProcedures).Count();
            var scCount = engine.Config.Schema.Count();
            reporter.Output($"Pulled {spCount} StoredProcedures from {scCount} Schemas in {stopwatch.ElapsedMilliseconds} ms.");

            if (!dryrun)
                engine.SaveConfigFile(engine.Config);

            return (int)ExecuteResultEnum.Succeeded;
        }

        private static int OnBuild(CommandLineInterface cli, bool dryrun)
        {
            var engine = cli.ServiceProvider.GetService<Engine>();
            var reporter = cli.ServiceProvider.GetService<IReporter>();

            if(dryrun) 
            {
                reporter.Output($"Build as dry run.");
            }

            if (!engine.ConfigFileExists())
            {
                reporter.Error($"File not found: {Configuration.ConfigurationFile}");
                reporter.Output($"\tPlease make sure you are in the right working directory");
                return (int)ExecuteResultEnum.Error;
            }

            if (!(engine.Config?.Schema?.Any() ?? false))
            {
                reporter.Error($"Schema is empty: {Configuration.ConfigurationFile}");
                reporter.Output($"\tPlease run pull to get the DB-Schema.");
                return (int)ExecuteResultEnum.Error;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            if(engine.Config.Project.Role.Kind != ERoleKind.Extension)
            {
                // we dont have a codebase, so generate it
                engine.GenerateCodeBase(dryrun);
                
                reporter.Output($"CodeBase generated in {stopwatch.ElapsedMilliseconds} ms.");
            }

            if(engine.Config.Project.Role.Kind == ERoleKind.Lib) 
            {
                // its only a lib
                return (int)ExecuteResultEnum.Succeeded;
            }

            stopwatch.Restart();

            engine.GenerateDataContextModels(dryrun);

            reporter.Output($"DataContextModels generated in {stopwatch.ElapsedMilliseconds} ms.");

            stopwatch.Restart();

            engine.GenerateDataContextParams(dryrun);

            reporter.Output($"DataContextParams generated in {stopwatch.ElapsedMilliseconds} ms.");

            stopwatch.Restart();

            engine.GenerateDataContextStoredProcedures(dryrun);

            reporter.Output($"DataContextStoredProcedures generated in {stopwatch.ElapsedMilliseconds} ms.");

            return (int)ExecuteResultEnum.Succeeded;
        }

        private static int OnRemove(CommandLineInterface cli, bool dryrun)
        {
            var engine = cli.ServiceProvider.GetService<Engine>();
            var reporter = cli.ServiceProvider.GetService<IReporter>();

            if(dryrun) 
            {
                reporter.Output($"Remove as dry run.");
            }

            var proceed1 = Prompt.GetYesNo("Remove all generated files?", true);
            if (!proceed1) return (int)ExecuteResultEnum.Aborted;

            engine.RemoveGeneratedFiles();

            reporter.Output($"Generated folder and files removed.");

            var proceed2 = Prompt.GetYesNo($"Remove {Configuration.ConfigurationFile}?", true);
            if (!proceed2) return (int)ExecuteResultEnum.Aborted;

            engine.RemoveConfig();

            reporter.Output($"{Configuration.ConfigurationFile} removed.");

            return (int)ExecuteResultEnum.Succeeded;
        }
    }

    internal enum ExecuteResultEnum
    {
        Undefined = 0,
        Succeeded = 1,
        Aborted = -1,
        Error = -9,
        Exception = -99
    }

    internal static class CommandLineInterfaceServiceCollectionExtensions
    {
        internal static IServiceCollection AddCommandLineInterface(this IServiceCollection services)
        {
            services.AddSingleton<CommandLineInterface>();
            return services;
        }
    }
}
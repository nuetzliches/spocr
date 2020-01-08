using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using SpocR.DataContext;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers
{
    public class SpocrManager
    {
        private readonly IConfiguration _configuration;
        private readonly SpocrService _spocr;
        private readonly OutputService _output;
        private readonly Generator _engine;
        private readonly IReporter _reporter;
        private readonly IReportService _reportService;
        private readonly SchemaManager _schemaManager;
        private readonly FileManager<GlobalConfigurationModel> _globalConfigFile;
        private readonly FileManager<ConfigurationModel> _configFile;
        private readonly DbContext _dbContext;

        public SpocrManager(
            IConfiguration configuration,
            SpocrService spocr,
            OutputService output,
            Generator engine,
            IReporter reporter,
            IReportService reportService,
            SchemaManager schemaManager,
            FileManager<GlobalConfigurationModel> globalConfigFile,
            FileManager<ConfigurationModel> configFile,
            DbContext dbContext
        )
        {
            _configuration = configuration;
            _spocr = spocr;
            _output = output;
            _engine = engine;
            _reportService = reportService;
            _schemaManager = schemaManager;
            _globalConfigFile = globalConfigFile;
            _configFile = configFile;
            _dbContext = dbContext;
        }

        public ExecuteResultEnum Create(bool isDryRun)
        {
            if (_configFile.Exists())
            {
                _reportService.Error($"File already exists: {Configuration.ConfigurationFile}");
                _reportService.Output($"\tPlease run: {Configuration.Name} status");
                return ExecuteResultEnum.Error;
            }

            var proceed = Prompt.GetYesNo("Create a new SpocR Project?", true);
            if (!proceed) return ExecuteResultEnum.Aborted;

            var appNamespace = Prompt.GetString("Your Project Namespace:", new DirectoryInfo(Directory.GetCurrentDirectory()).Name);

            // var configurationFileExists = _configuration.FileExists();
            // if(!configurationFileExists) 
            // {
            //     var fileName = Extensions.ConfigurationExtensions.FileName;
            //     var proceedAppsettings = Prompt.GetYesNo("Create a new SpocR Project?", true);
            //     if (!proceedAppsettings) return ExecuteResultEnum.Aborted;
            // }

            // Prompt.OnSelection("Please choose an option", )
            // var optionKey = 1;
            // foreach(var identifier in _configuration.GetSection("ConnectionStrings").GetChildren()) {
            //     _reportService.Output($"{optionKey}");            
            // }
            var connectionString = "";

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

            var identityKindString = Prompt.GetString("SpocR Identity [WithUserId, None]:", "WithUserId");
            var identityKind = default(EIdentityKind);
            Enum.TryParse(identityKindString, true, out identityKind);

            var identity = new IdentityModel
            {
                Kind = identityKind
            };

            var config = new ConfigurationModel
            {
                Version = _spocr.Version,
                Modified = DateTime.Now,
                Project = new ProjectModel
                {
                    Role = role,
                    Identity = identity,
                    DataBase = new DataBaseModel
                    {
                        // the default appsettings.json ConnectString Identifier
                        // you can customize this one later on in the spocr.json
                        RuntimeConnectionStringIdentifier = "DefaultConnection",
                        ConnectionString = connectionString ?? ""
                    },
                    Output = new OutputModel
                    {
                        Namespace = appNamespace,
                        DataContext = new DataContextModel
                        {
                            Path = "./DataContext",
                            Models = new DataContextModelsModel
                            {
                                Path = "./Models",
                            },
                            Params = new DataContextParamsModel
                            {
                                Path = "./Params",
                            },
                            StoredProcedures = new DataContextStoredProceduresModel
                            {
                                Path = "./StoredProcedures",
                            }
                        }
                    }
                },
                Schema = new List<SchemaModel>()
            };

            if (isDryRun)
            {
                _reportService.PrintConfiguration(config);
                _reportService.PrintDryRunMessage();
            }
            else
            {
                _configFile.Save(config);
                _reportService.Output($"{Configuration.Name} successfully created.");
            }

            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum Pull(bool isDryRun)
        {
            _reportService.PrintTitle("Pulling DB-Schema from Database");

            if (!_configFile.Exists())
            {
                _reportService.Error($"File not found: {Configuration.ConfigurationFile}");
                _reportService.Output($"\tPlease make sure you are in the right working directory");
                return ExecuteResultEnum.Error;
            }

            var userConfigFileName = Configuration.UserConfigurationFile.Replace("{userId}", _globalConfigFile.Config?.UserId);
            var userConfigFile = new FileManager<ConfigurationModel>(userConfigFileName);

            if (userConfigFile.Exists())
            {
                var userConfig = userConfigFile.Read();
                _configFile.OverwriteWithConfig = userConfig;
            }

            if (!string.IsNullOrWhiteSpace(_configFile.Config?.Project?.DataBase?.ConnectionString))
            {
                _dbContext.SetConnectionString(_configFile.Config.Project.DataBase.ConnectionString);
            }

            if (string.IsNullOrWhiteSpace(_configFile.Config.Project.DataBase.ConnectionString))
            {
                _reportService.Error($"ConnectionString is empty: {Configuration.ConfigurationFile}");
                _reportService.Output($"\tPlease run {Configuration.Name} set --cs <ConnectionString>");
                return ExecuteResultEnum.Error;
            }

            var config = _configFile.Config;
            var configSchemas = config?.Schema?.ToList() ?? new List<SchemaModel>();

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            _schemaManager.ListAsync(true, config).ContinueWith(t =>
            {
                var result = t.Result;
                var overwriteWithCurrentConfig = configSchemas.Any();
                if (overwriteWithCurrentConfig)
                {
                    foreach (var schema in result)
                    {
                        var currentSchema = configSchemas.SingleOrDefault(i => i.Name == schema.Name);
                        schema.Status = currentSchema != null ? currentSchema.Status : SchemaStatusEnum.Build;
                    }
                }
                configSchemas = result;

            }).Wait();

            var pullSchemas = configSchemas.Where(x => x.Status == SchemaStatusEnum.Build);
            var ignoreSchemas = configSchemas.Where(x => x.Status == SchemaStatusEnum.Ignore);

            var pulledStoredProcedures = pullSchemas.SelectMany(x => x.StoredProcedures ?? new List<StoredProcedureModel>()).ToList();
            var pulledSchemasWithStoredProcedures = pullSchemas
                .Select(x => new
                {
                    Schema = x,
                    StoredProcedures = x.StoredProcedures.ToList() ?? new List<StoredProcedureModel>()
                }).ToList();

            pulledSchemasWithStoredProcedures.ForEach(schema =>
            {
                schema.StoredProcedures.ForEach((sp => _reportService.Verbose($"PULL: [{schema.Schema.Name}].[{sp.Name}]")));
            });
            _reportService.Output("");

            if (ignoreSchemas.Any())
            {
                _reportService.Error($"Ignored {ignoreSchemas.Count()} Schemas [{string.Join(", ", ignoreSchemas.Select(x => x.Name))}]");
                _reportService.Output("");
            }

            _reportService.Yellow($"Pulled {pulledStoredProcedures.Count()} StoredProcedures from {pullSchemas.Count()} Schemas [{string.Join(", ", pullSchemas.Select(x => x.Name))}] in {stopwatch.ElapsedMilliseconds} ms.");
            _reportService.Output("");

            if (isDryRun)
            {
                _reportService.PrintDryRunMessage();
            }
            else
            {
                config.Schema = configSchemas;
                _configFile.Save(config);
            }

            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum Build(bool isDryRun)
        {
            _reportService.PrintTitle("Build DataContext from spocr.json");
            
            if (!_configFile.Exists())
            {
                _reportService.Error($"Config file not found: {Configuration.ConfigurationFile}");   // why do we use Configuration here?
                _reportService.Output($"\tPlease make sure you are in the right working directory");
                return ExecuteResultEnum.Error;
            }

            var config = _configFile.Config;
            var project = config?.Project;
            var schemas = config?.Schema;
            var connectionString = project?.DataBase?.ConnectionString;

            var hasProject = project != null;
            var hasSchemas = schemas?.Any() ?? false;
            var hasConnectionString = string.IsNullOrWhiteSpace(connectionString);

            if (!hasConnectionString)
            {
                _dbContext.SetConnectionString(connectionString);
            }

            if (!hasSchemas)
            {
                _reportService.Error($"Schema is empty: {Configuration.ConfigurationFile}"); // why do we use Configuration here?
                _reportService.Output($"\tPlease run pull to get the DB-Schema.");
                return ExecuteResultEnum.Error;
            }

            var stopwatch = new Stopwatch();
            var elapsed = new Dictionary<string, long>();

            var existsCodeBase = project.Role.Kind == ERoleKind.Extension;
            if (!existsCodeBase)
            {
                stopwatch.Start();
                _reportService.PrintSubTitle("Generating CodeBase");
                _output.GenerateCodeBase(project.Output, isDryRun);
                elapsed.Add("CodeBase", stopwatch.ElapsedMilliseconds);
            }

            stopwatch.Restart();
            _reportService.PrintSubTitle("Generating Models");
            _engine.GenerateDataContextModels(isDryRun);
            elapsed.Add("Models", stopwatch.ElapsedMilliseconds);

            stopwatch.Restart();
            _reportService.PrintSubTitle("Generating Params");
            _engine.GenerateDataContextParams(isDryRun);
            elapsed.Add("Params", stopwatch.ElapsedMilliseconds);

            stopwatch.Restart();
            _reportService.PrintSubTitle("Generating StoredProcedures");
            _engine.GenerateDataContextStoredProcedures(isDryRun);
            elapsed.Add("StoredProcedures", stopwatch.ElapsedMilliseconds);

            _reportService.PrintSummary(elapsed.Select(_ => $"{_.Key} generated in {_.Value} ms."));
            _reportService.PrintTotal($"Total elapsed time: {elapsed.Sum(_ => _.Value)} ms.");

            if (isDryRun) 
            {
                _reportService.PrintDryRunMessage();
            }

            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum Remove(bool dryRun)
        {
            if (dryRun)
            {
                _reportService.Output($"Remove as dry run.");
            }

            var proceed1 = Prompt.GetYesNo("Remove all generated files?", true);
            if (!proceed1) return ExecuteResultEnum.Aborted;

            _output.RemoveGeneratedFiles(_configFile.Config.Project.Output.DataContext.Path, dryRun);

            _reportService.Output($"Generated folder and files removed.");

            var proceed2 = Prompt.GetYesNo($"Remove {Configuration.ConfigurationFile}?", true);
            if (!proceed2) return ExecuteResultEnum.Aborted;

            _configFile.Remove(dryRun);

            _reportService.Output($"{Configuration.ConfigurationFile} removed.");

            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum GetVersion()
        {
            _reportService.Output($"Version: {_spocr.Version.ToVersionString()}.");

            return ExecuteResultEnum.Succeeded;
        }
    }
}
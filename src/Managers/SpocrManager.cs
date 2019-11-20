using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SpocR.DataContext;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Models;
using SpocR.Serialization;
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
            _reporter = reporter;
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
                _reporter.Error($"File already exists: {Configuration.ConfigurationFile}");
                _reporter.Output($"\tPlease run: {Configuration.Name} status");
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
            //     _reporter.Output($"{optionKey}");            
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
                _reporter.Output($"{Configuration.Name} successfully created.");
            }

            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum Pull(bool isDryRun)
        {
            if (!_configFile.Exists())
            {
                _reporter.Error($"File not found: {Configuration.ConfigurationFile}");
                _reporter.Output($"\tPlease make sure you are in the right working directory");
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
                _reporter.Error($"ConnectionString is empty: {Configuration.ConfigurationFile}");
                _reporter.Output($"\tPlease run {Configuration.Name} set --cs <ConnectionString>");
                return ExecuteResultEnum.Error;
            }

            var config = _configFile.Config;
            var configSchemas = config?.Schema.ToList() ?? new List<SchemaModel>();

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
                        var currentSchema = configSchemas.FirstOrDefault(i => i.Id == schema.Id);
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
                schema.StoredProcedures.ForEach((sp => _reporter.Verbose($"PULL: [{schema.Schema.Name}].[{sp.Name}]")));
            });
            _reporter.Output("");

            if (ignoreSchemas.Any())
            {
                _reporter.Error($"Ignored {ignoreSchemas.Count()} Schemas [{string.Join(", ", ignoreSchemas.Select(x => x.Name))}]");
                _reporter.Output("");
            }

            _reporter.Output($"Pulled {pulledStoredProcedures.Count()} StoredProcedures from {pullSchemas.Count()} Schemas [{string.Join(", ", pullSchemas.Select(x => x.Name))}] in {stopwatch.ElapsedMilliseconds} ms.");
            _reporter.Output("");

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
            if (!_configFile.Exists())
            {
                _reporter.Error($"Config file not found: {Configuration.ConfigurationFile}");   // why do we use Configuration here?
                _reporter.Output($"\tPlease make sure you are in the right working directory");
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
                _reporter.Error($"Schema is empty: {Configuration.ConfigurationFile}"); // why do we use Configuration here?
                _reporter.Output($"\tPlease run pull to get the DB-Schema.");
                return ExecuteResultEnum.Error;
            }

            var stopwatch = new Stopwatch();

            // var hasNoCodeBase = project.Role.Kind != ERoleKind.Extension;
            var existsCodeBase = project.Role.Kind == ERoleKind.Extension;
            if (!existsCodeBase)
            {
                stopwatch.Start();
                // we dont have a codebase, so generate it
                _output.GenerateCodeBase(project.Output, isDryRun);
                _reporter.Output($"CodeBase generated in {stopwatch.ElapsedMilliseconds} ms.");
            }

            // We would have StoredProcedures and Models inside the Lib too
            // if (_configFile.Config.Project.Role.Kind == ERoleKind.Lib)
            // {
            //     // its only a lib
            //     return ExecuteResultEnum.Succeeded;
            // }

            stopwatch.Restart();

            _engine.GenerateDataContextModels(isDryRun);

            _reporter.Output("");
            _reporter.Output($"DataContextModels generated in {stopwatch.ElapsedMilliseconds} ms.");

            stopwatch.Restart();

            _engine.GenerateDataContextParams(isDryRun);

            _reporter.Output("");
            _reporter.Output($"DataContextParams generated in {stopwatch.ElapsedMilliseconds} ms.");

            stopwatch.Restart();

            _engine.GenerateDataContextStoredProcedures(isDryRun);

            _reporter.Output("");
            _reporter.Output($"DataContextStoredProcedures generated in {stopwatch.ElapsedMilliseconds} ms.");

            if (isDryRun) 
            {
                _reporter.Output("");
                _reportService.PrintDryRunMessage();
            }

            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum Remove(bool dryRun)
        {
            if (dryRun)
            {
                _reporter.Output($"Remove as dry run.");
            }

            var proceed1 = Prompt.GetYesNo("Remove all generated files?", true);
            if (!proceed1) return ExecuteResultEnum.Aborted;

            _output.RemoveGeneratedFiles(_configFile.Config.Project.Output.DataContext.Path, dryRun);

            _reporter.Output($"Generated folder and files removed.");

            var proceed2 = Prompt.GetYesNo($"Remove {Configuration.ConfigurationFile}?", true);
            if (!proceed2) return ExecuteResultEnum.Aborted;

            _configFile.Remove(dryRun);

            _reporter.Output($"{Configuration.ConfigurationFile} removed.");

            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum GetVersion()
        {
            _reporter.Output($"Version: {_spocr.Version.ToVersionString()}.");

            return ExecuteResultEnum.Succeeded;
        }
    }
}
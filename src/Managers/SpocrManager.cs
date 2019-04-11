using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Commands;
using SpocR.Enums;
using SpocR.Internal.Common;
using SpocR.Internal.Managers;
using SpocR.Internal.Models;
using SpocR.Services;

namespace SpocR.Managers
{
    public class SpocrManager
    {
        private readonly SpocrService _spocr;
        private readonly OutputService _output;
        private readonly Engine _engine;
        private readonly IReporter _reporter;
        private readonly SchemaManager _schemaManager;
        private readonly ConfigFileManager _configFile;


        public SpocrManager(SpocrService spocr, OutputService output, Engine engine, IReporter reporter, SchemaManager schemaManager, ConfigFileManager configFile)
        {
            _spocr = spocr;
            _output = output;
            _engine = engine;
            _reporter = reporter;
            _schemaManager = schemaManager;
            _configFile = configFile;
        }

        public ExecuteResultEnum Create(bool dryRun)
        {
            if (_configFile.Exists())
            {
                _reporter.Error($"File already exists: {Configuration.ConfigurationFile}");
                _reporter.Output($"\tPlease run: {Configuration.Name} status");
                return ExecuteResultEnum.Error;
            }

            if (dryRun)
            {
                _reporter.Output($"Create as dry run.");
            }

            var proceed = Prompt.GetYesNo("Create a new SpocR Project?", true);
            if (!proceed) return ExecuteResultEnum.Aborted;

            var appNamespace = Prompt.GetString("Your Project Namespace:", new DirectoryInfo(Directory.GetCurrentDirectory()).Name);
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
                Version = _spocr.Version,
                Modified = DateTime.Now,
                Project = new ProjectModel
                {
                    Role = role,
                    DataBase = new DataBaseModel
                    {
                        // the default appsettings.json ConnectString Identifier
                        // you can customize this one later on in the spocr.json
                        RuntimeConnectionStringIdentifier = "DefaultConnection",
                        ConnectionString = connectionString ?? ""
                    },
                    Output = new OutputModel {
                        Namespace = appNamespace,
                        DataContext = new DataContextModel {
                            Path = "./DataContext",
                            Models = new DataContextModelsModel {
                                Path = "./Models",
                            },
                            Params = new DataContextParamsModel {
                                Path = "./Params",
                            },
                            StoredProcedures = new DataContextStoredProceduresModel {
                                Path = "./StoredProcedures",
                            }
                        }
                    }
                },
                Schema = new List<SchemaModel>()
            };

            _configFile.Save(config);
            _reporter.Output($"{Configuration.Name} successfully created.");

            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum Pull(bool dryRun)
        {
            if (dryRun)
            {
                _reporter.Output($"Pull as dry run.");
            }

            if (!_configFile.Exists())
            {
                _reporter.Error($"File not found: {Configuration.ConfigurationFile}");
                _reporter.Output($"\tPlease make sure you are in the right working directory");
                return ExecuteResultEnum.Error;
            }

            if (string.IsNullOrWhiteSpace(_configFile.Config.Project.DataBase.ConnectionString))
            {
                _reporter.Error($"ConnectionString is empty: {Configuration.ConfigurationFile}");
                _reporter.Output($"\tPlease run {Configuration.Name} set --cs <ConnectionString>");
                return ExecuteResultEnum.Error;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            _schemaManager.ListAsync(true, _configFile.Config).ContinueWith(t =>
            {
                var result = t.Result;
                // overwrite with current config
                if (_configFile.Config?.Schema != null)
                {
                    foreach (var schema in result)
                    {
                        var currentSchema = _configFile.Config.Schema.FirstOrDefault(i => i.Id == schema.Id);
                        schema.Status = currentSchema != null ? currentSchema.Status : SchemaStatusEnum.Build;
                    }
                }
                _configFile.Config.Schema = result;

            }).Wait();

            var spCount = _configFile.Config.Schema.SelectMany(x => x.StoredProcedures).Count();
            var scCount = _configFile.Config.Schema.Count();
            _reporter.Output($"Pulled {spCount} StoredProcedures from {scCount} Schemas in {stopwatch.ElapsedMilliseconds} ms.");

            if (!dryRun)
                _configFile.Save(_configFile.Config);

            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum Build(bool dryRun)
        {
            if (dryRun)
            {
                _reporter.Output($"Build as dry run.");
            }

            if (!_configFile.Exists())
            {
                _reporter.Error($"File not found: {Configuration.ConfigurationFile}");
                _reporter.Output($"\tPlease make sure you are in the right working directory");
                return ExecuteResultEnum.Error;
            }

            if (!(_configFile.Config?.Schema?.Any() ?? false))
            {
                _reporter.Error($"Schema is empty: {Configuration.ConfigurationFile}");
                _reporter.Output($"\tPlease run pull to get the DB-Schema.");
                return ExecuteResultEnum.Error;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            if (_configFile.Config.Project.Role.Kind != ERoleKind.Extension)
            {
                // we dont have a codebase, so generate it
                _output.GenerateCodeBase(_configFile.Config.Project.Output, dryRun);

                _reporter.Output($"CodeBase generated in {stopwatch.ElapsedMilliseconds} ms.");
            }

            if (_configFile.Config.Project.Role.Kind == ERoleKind.Lib)
            {
                // its only a lib
                return ExecuteResultEnum.Succeeded;
            }

            stopwatch.Restart();

            _engine.GenerateDataContextModels(dryRun);

            _reporter.Output($"DataContextModels generated in {stopwatch.ElapsedMilliseconds} ms.");

            stopwatch.Restart();

            _engine.GenerateDataContextParams(dryRun);

            _reporter.Output($"DataContextParams generated in {stopwatch.ElapsedMilliseconds} ms.");

            stopwatch.Restart();

            _engine.GenerateDataContextStoredProcedures(dryRun);

            _reporter.Output($"DataContextStoredProcedures generated in {stopwatch.ElapsedMilliseconds} ms.");

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

            _engine.RemoveGeneratedFiles();

            _reporter.Output($"Generated folder and files removed.");

            var proceed2 = Prompt.GetYesNo($"Remove {Configuration.ConfigurationFile}?", true);
            if (!proceed2) return ExecuteResultEnum.Aborted;

            _configFile.Remove();

            _reporter.Output($"{Configuration.ConfigurationFile} removed.");

            return ExecuteResultEnum.Succeeded;
        }
    }
}
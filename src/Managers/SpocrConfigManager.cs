using System;
using System.Linq;
using System.Reflection;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using SpocR.Attributes;
using SpocR.DataContext;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers
{
    public class SpocrConfigManager
    {
        private readonly IConfiguration _configuration;
        private readonly SpocrService _spocr;
        private readonly OutputService _output;
        private readonly Generator _engine;
        private readonly IReporter _reporter;
        private readonly SchemaManager _schemaManager;
        private readonly FileManager<GlobalConfigurationModel> _globalConfigFile;
        private readonly FileManager<ConfigurationModel> _configFile;
        private readonly DbContext _dbContext;

        public SpocrConfigManager(
            IConfiguration configuration, 
            SpocrService spocr, 
            OutputService output, 
            Generator engine, 
            IReporter reporter,
            SchemaManager schemaManager, 
            FileManager<GlobalConfigurationModel> globalConfigFile, 
            FileManager<ConfigurationModel> configFile, 
            DbContext dbContext)
        {
            _configuration = configuration;
            _spocr = spocr;
            _output = output;
            _engine = engine;
            _reporter = reporter;
            _schemaManager = schemaManager;
            _globalConfigFile = globalConfigFile;
            _dbContext = dbContext;
        }

        public ExecuteResultEnum Config()
        {
            if (!_globalConfigFile.Exists())
            {
                _reporter.Error($"Global config is missing!");
            } 

            var config = _globalConfigFile.Read();

            var propertyInfos = typeof(GlobalConfigurationModel)
                                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                    .Where(prop => !(prop.GetCustomAttribute<WriteProtectedBySystem>()?.IsProtected ?? false));

            _reporter.Warn("Please enter your Configuration:");
            
            foreach(var prop in propertyInfos) 
            {
                var input = Prompt.GetString(prop.Name, prop.GetValue(config)?.ToString());
                prop.SetValue(config, input);
            }

            var proceed = Prompt.GetYesNo("Write your entries to GlobalConfigFile?", true, ConsoleColor.Red);
            if (!proceed) return ExecuteResultEnum.Aborted;

            _globalConfigFile.Save(config);

            // var proceed = Prompt.GetYesNo("Create a new SpocR Project?", true);
            // if (!proceed) return ExecuteResultEnum.Aborted;

            // var appNamespace = Prompt.GetString("Your Project Namespace:", new DirectoryInfo(Directory.GetCurrentDirectory()).Name);

            // // var configurationFileExists = _configuration.FileExists();
            // // if(!configurationFileExists) 
            // // {
            // //     var fileName = Extensions.ConfigurationExtensions.FileName;
            // //     var proceedAppsettings = Prompt.GetYesNo("Create a new SpocR Project?", true);
            // //     if (!proceedAppsettings) return ExecuteResultEnum.Aborted;
            // // }

            // // Prompt.OnSelection("Please choose an option", )
            // // var optionKey = 1;
            // // foreach(var identifier in _configuration.GetSection("ConnectionStrings").GetChildren()) {
            // //     _reporter.Output($"{optionKey}");            
            // // }
            // var connectionString = "";

            // var roleKindString = Prompt.GetString("SpocR Role [Default, Lib, Extension]:", "Default");
            // var roleKind = default(ERoleKind);
            // Enum.TryParse(roleKindString, true, out roleKind);

            // var role = new RoleModel
            // {
            //     Kind = roleKind,
            //     LibNamespace = roleKind == ERoleKind.Extension
            //         ? Prompt.GetString("SpocR Lib Namespace:", "Nuts.DbContext")
            //         : null
            // };

            // var identityKindString = Prompt.GetString("SpocR Identity [WithUserId, None]:", "WithUserId");
            // var identityKind = default(EIdentityKind);
            // Enum.TryParse(identityKindString, true, out identityKind);

            // var identity = new IdentityModel
            // {
            //     Kind = identityKind
            // };

            // var config = new ConfigurationModel
            // {
            //     Version = _spocr.Version,
            //     Modified = DateTime.Now,
            //     Project = new ProjectModel
            //     {
            //         Role = role,
            //         Identity = identity,
            //         DataBase = new DataBaseModel
            //         {
            //             // the default appsettings.json ConnectString Identifier
            //             // you can customize this one later on in the spocr.json
            //             RuntimeConnectionStringIdentifier = "DefaultConnection",
            //             ConnectionString = connectionString ?? ""
            //         },
            //         Output = new OutputModel {
            //             Namespace = appNamespace,
            //             DataContext = new DataContextModel {
            //                 Path = "./DataContext",
            //                 Models = new DataContextModelsModel {
            //                     Path = "./Models",
            //                 },
            //                 Params = new DataContextParamsModel {
            //                     Path = "./Params",
            //                 },
            //                 StoredProcedures = new DataContextStoredProceduresModel {
            //                     Path = "./StoredProcedures",
            //                 }
            //             }
            //         }
            //     },
            //     Schema = new List<SchemaModel>()
            // };

            // _globalConfigFile.Save(config);
            // _reporter.Output($"{Configuration.Name} successfully created.");

            return ExecuteResultEnum.Succeeded;
        }
    }
}
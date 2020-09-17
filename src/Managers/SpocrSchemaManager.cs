using System.Linq;
using SpocR.Commands;
using SpocR.Commands.Schema;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.Managers
{
    public class SpocrSchemaManager
    {
        private readonly FileManager<ConfigurationModel> _configFile;
        private readonly IReportService _reportService;

        public SpocrSchemaManager(
            FileManager<ConfigurationModel> configFile,
            IReportService reportService
        )
        {
            _configFile = configFile;
            _reportService = reportService;
        }

        public ExecuteResultEnum Update(ISchemaUpdateCommandOptions options)
        {
            // var displayName = options;
            // var schemaIndex = FindIndexByName(displayName);

            // if (schemaIndex < 0)
            // {
            //     _reportService.Error($"Cant find schema '{displayName}'");
            //     return ExecuteResultEnum.Error;
            // }

            // var path = options.Path;
            // if (!string.IsNullOrEmpty(path))
            // {
            //     path = CreateConfigFilePath(path);
            // }

            // var newDisplayName = options.NewDisplayName;
            // if (!string.IsNullOrEmpty(newDisplayName))
            // {
            //     if (IsDisplayNameAlreadyUsed(newDisplayName, options))
            //     {
            //         return ExecuteResultEnum.Error;
            //     }
            // }

            // if (!string.IsNullOrEmpty(newDisplayName))
            // {
            //     _globalConfigFile.Config.Schemas[schemaIndex].DisplayName = newDisplayName;
            // }

            // if (!string.IsNullOrEmpty(path))
            // {
            //     _globalConfigFile.Config.Schemas[schemaIndex].ConfigFile = path;
            // }

            // _globalConfigFile.Save(_globalConfigFile.Config);

            // _reportService.Output($"Schema '{displayName}' updated.");
            return ExecuteResultEnum.Succeeded;

        }

        public ExecuteResultEnum List(ICommandOptions options)
        {
            // _configFile.Read();
            var schemas = _configFile.Config?.Schema?.ToList();

            if (!options.Silent && !(schemas?.Any() ?? false))
            {
                _reportService.Warn($"No Schemas found");
                return ExecuteResultEnum.Aborted;
            }

            _reportService.Output($"[{(schemas.Count > 0 ? "{" : "")}");
            schemas.ForEach(schema =>
            {
                _reportService.Output($"\t\"name\": \"{schema.Name}\",");
                _reportService.Output($"\t\"status\": \"{schema.Status}\"");
                if (schemas.FindIndex(_ => _ == schema) < schemas.Count - 1)
                {
                    _reportService.Output("}, {");
                }
            });
            _reportService.Output($"{(schemas.Count > 0 ? "}" : "")}]");

            return ExecuteResultEnum.Succeeded;
        }
    }
}
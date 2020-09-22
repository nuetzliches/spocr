using System;
using System.Linq;
using SpocR.Commands;
using SpocR.Commands.Schema;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

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
            var schemaName = options.SchemaName;
            var schemaIndex = FindIndexByName(schemaName);

            if (schemaIndex < 0)
            {
                _reportService.Error($"Cant find schema '{schemaName}'");
                return ExecuteResultEnum.Error;
            }

            var status = options.Status;
            if (!string.IsNullOrEmpty(status))
            {
                _configFile.Config.Schema[schemaIndex].Status = Enum.Parse<SchemaStatusEnum>(status);
            }

            _configFile.Save(_configFile.Config);

            _reportService.Output($"Schema '{schemaName}' updated.");
            return ExecuteResultEnum.Succeeded;

        }

        public ExecuteResultEnum List(ICommandOptions options)
        {
            // _configFile.Read();
            var schemas = _configFile.Config?.Schema;

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

        private int FindIndexByName(string schemaName)
        {
            var schemaList = _configFile.Config.Schema;
            return schemaList.FindIndex(schema => schema.Name.Equals(schemaName));
        }
    }
}
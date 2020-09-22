using System.Linq;
using SpocR.Commands.StoredProcdure;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers
{
    public class SpocrStoredProcdureManager
    {
        private readonly FileManager<ConfigurationModel> _configFile;
        private readonly IReportService _reportService;

        public SpocrStoredProcdureManager(
            FileManager<ConfigurationModel> configFile,
            IReportService reportService
        )
        {
            _configFile = configFile;
            _reportService = reportService;
        }

        public ExecuteResultEnum List(IStoredProcedureCommandOptions options)
        {
            var storedProcedures = _configFile.Config?.Schema.FirstOrDefault(_ => _.Name.Equals(options.SchemaName))?.StoredProcedures?.ToList();

            if (!(storedProcedures?.Any() ?? false))
            {
                if (!options.Silent)
                {
                    _reportService.Warn($"No StoredProcduress found");
                }
                return ExecuteResultEnum.Aborted;
            }

            _reportService.Output($"[{(storedProcedures.Count > 0 ? "{" : "")}");
            storedProcedures.ForEach(storedprocdures =>
            {
                _reportService.Output($"\t\"name\": \"{storedprocdures.Name}\"");
                // _reportService.Output($"\t\"name\": \"{storedprocdures.Name}\",");
                // _reportService.Output($"\t\"status\": \"{storedprocdures.Status}\"");
                if (storedProcedures.FindIndex(_ => _ == storedprocdures) < storedProcedures.Count - 1)
                {
                    _reportService.Output("}, {");
                }
            });
            _reportService.Output($"{(storedProcedures.Count > 0 ? "}" : "")}]");

            return ExecuteResultEnum.Succeeded;
        }
    }
}
using System.Linq;
using SpocR.Commands.StoredProcdure;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SpocrStoredProcdureManager(
    FileManager<ConfigurationModel> configFile,
    IReportService reportService
)
{
    public ExecuteResultEnum List(IStoredProcedureCommandOptions options)
    {
        var storedProcedures = configFile.Config?.Schema.FirstOrDefault(_ => _.Name.Equals(options.SchemaName))?.StoredProcedures?.ToList();

        if (!(storedProcedures?.Any() ?? false))
        {
            if (!options.Silent)
            {
                reportService.Warn($"No StoredProcduress found");
            }
            return ExecuteResultEnum.Aborted;
        }

        reportService.Output($"[{(storedProcedures.Count > 0 ? "{" : "")}");
        storedProcedures.ForEach(storedprocdures =>
        {
            reportService.Output($"\t\"name\": \"{storedprocdures.Name}\"");
            // _reportService.Output($"\t\"name\": \"{storedprocdures.Name}\",");
            // _reportService.Output($"\t\"status\": \"{storedprocdures.Status}\"");
            if (storedProcedures.FindIndex(_ => _ == storedprocdures) < storedProcedures.Count - 1)
            {
                reportService.Output("}, {");
            }
        });
        reportService.Output($"{(storedProcedures.Count > 0 ? "}" : "")}]");

        return ExecuteResultEnum.Succeeded;
    }
}
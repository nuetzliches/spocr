using System.Linq;
using System.Text.Json;
using SpocR.Commands.StoredProcedure;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SpocrStoredProcedureManager(
    IConsoleService consoleService
)
{
    public ExecuteResultEnum List(IStoredProcedureCommandOptions options)
    {
        var configFile = new FileManager<ConfigurationModel>(null, Constants.ConfigurationFile);
        if (!configFile.TryOpen(options.Path, out ConfigurationModel config))
        {
            // Keine Config -> leere JSON Liste zurückgeben (bleibt dennoch Aborted um bestehendes Verhalten nicht zu brechen)
            consoleService.Output("[]");
            if (!options.Quiet && !options.Json)
            {
                consoleService.Warn("No configuration file found");
            }
            return ExecuteResultEnum.Aborted;
        }

        var schema = config?.Schema.FirstOrDefault(_ => _.Name.Equals(options.SchemaName));
        if (schema == null)
        {
            consoleService.Output("[]");
            if (!options.Quiet && !options.Json)
            {
                consoleService.Warn($"Schema '{options.SchemaName}' not found");
            }
            return ExecuteResultEnum.Aborted;
        }

        var storedProcedures = schema.StoredProcedures?.ToList();

        if (!(storedProcedures?.Any() ?? false))
        {
            // Leere Liste – immer valides JSON ausgeben
            consoleService.Output("[]");
            if (!options.Quiet && !options.Json)
            {
                consoleService.Warn("No StoredProcedures found");
            }
            return ExecuteResultEnum.Aborted; // Beibehaltung des bisherigen Exit Codes
        }
        var json = JsonSerializer.Serialize(
            storedProcedures.Select(sp => new { name = sp.Name }),
            new JsonSerializerOptions { WriteIndented = false }
        );
        consoleService.Output(json);

        return ExecuteResultEnum.Succeeded;
    }
}

using System.Linq;
using System.Text.Json;
using SpocR.Commands.StoredProcedure;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SpocrStoredProcedureManager
{
    private readonly IConsoleService _consoleService;
    private readonly IFileManager<ConfigurationModel> _configFile;

    public SpocrStoredProcedureManager(IConsoleService consoleService, IFileManager<ConfigurationModel> configFile = null)
    {
        _consoleService = consoleService;
        _configFile = configFile != null
            ? configFile
            : new FileManager<ConfigurationModel>(null, Constants.ConfigurationFile);
    }

    public ExecuteResultEnum List(IStoredProcedureCommandOptions options)
    {
        if (!_configFile.TryOpen(options.Path, out ConfigurationModel config))
        {
            // Keine Config -> leere JSON Liste zurückgeben (bleibt dennoch Aborted um bestehendes Verhalten nicht zu brechen)
            _consoleService.Output("[]");
            if (!options.Quiet && !options.Json)
            {
                _consoleService.Warn("No configuration file found");
            }
            return ExecuteResultEnum.Aborted;
        }

        var schema = config?.Schema.FirstOrDefault(_ => _.Name.Equals(options.SchemaName));
        if (schema == null)
        {
            _consoleService.Output("[]");
            if (!options.Quiet && !options.Json)
            {
                _consoleService.Warn($"Schema '{options.SchemaName}' not found");
            }
            return ExecuteResultEnum.Aborted;
        }

        var storedProcedures = schema.StoredProcedures?.ToList();

        if (!(storedProcedures?.Any() ?? false))
        {
            // Leere Liste – immer valides JSON ausgeben
            _consoleService.Output("[]");
            if (!options.Quiet && !options.Json)
            {
                _consoleService.Warn("No StoredProcedures found");
            }
            return ExecuteResultEnum.Aborted; // Beibehaltung des bisherigen Exit Codes
        }
        var json = JsonSerializer.Serialize(
            storedProcedures.Select(sp => new { name = sp.Name }),
            new JsonSerializerOptions { WriteIndented = false }
        );
        _consoleService.Output(json);

        return ExecuteResultEnum.Succeeded;
    }
}

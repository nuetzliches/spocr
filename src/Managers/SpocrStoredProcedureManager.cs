using System.Linq;
using SpocR.Commands.StoredProcdure;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SpocrStoredProcdureManager(
    IConsoleService consoleService
)
{
    public EExecuteResult List(IStoredProcedureCommandOptions options)
    {
        var configFile = new FileManager<ConfigurationModel>(null, Constants.ConfigurationFile);
        if (!configFile.TryOpen(options.Path, out ConfigurationModel config))
        {
            consoleService.Warn($"No StoredProcduress found");
            return EExecuteResult.Aborted;
        }

        var storedProcedures = config?.Schema.FirstOrDefault(_ => _.Name.Equals(options.SchemaName))?.StoredProcedures?.ToList();

        if (!(storedProcedures?.Any() ?? false))
        {
            if (!options.Quiet)
            {
                consoleService.Warn($"No StoredProcduress found");
            }
            return EExecuteResult.Aborted;
        }

        consoleService.Output($"[{(storedProcedures.Count > 0 ? "{" : "")}]");
        storedProcedures.ForEach(storedprocdures =>
        {
            consoleService.Output($"\t\"name\": \"{storedprocdures.Name}\"");
            if (storedProcedures.FindIndex(_ => _ == storedprocdures) < storedProcedures.Count - 1)
            {
                consoleService.Output("}, {");
            }
        });
        consoleService.Output($"{(storedProcedures.Count > 0 ? "}" : "")}]");

        return EExecuteResult.Succeeded;
    }
}

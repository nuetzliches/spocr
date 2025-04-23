using System;
using System.Linq;
using System.Threading.Tasks;
using SpocR.Commands;
using SpocR.Commands.Schema;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SpocrSchemaManager(
    FileManager<ConfigurationModel> configFile,
    IConsoleService consoleService
)
{
    public async Task<EExecuteResult> UpdateAsync(ISchemaUpdateCommandOptions options)
    {
        var schemaName = options.SchemaName;
        var schemaIndex = FindIndexByName(schemaName);

        if (schemaIndex < 0)
        {
            consoleService.Error($"Cant find schema '{schemaName}'");
            return EExecuteResult.Error;
        }

        var status = options.Status;
        if (!string.IsNullOrEmpty(status))
        {
            configFile.Config.Schema[schemaIndex].Status = Enum.Parse<SchemaStatusEnum>(status);
        }

        await Task.Run(() => configFile.Save(configFile.Config));

        consoleService.Output($"Schema '{schemaName}' updated.");
        return EExecuteResult.Succeeded;
    }

    // Behalte die synchrone Methode für Abwärtskompatibilität
    public EExecuteResult Update(ISchemaUpdateCommandOptions options)
    {
        return UpdateAsync(options).GetAwaiter().GetResult();
    }

    public async Task<EExecuteResult> ListAsync(ICommandOptions options)
    {
        var schemas = configFile.Config?.Schema;

        if (!options.Quiet && !(schemas?.Any() ?? false))
        {
            consoleService.Warn($"No Schemas found");
            return EExecuteResult.Aborted;
        }

        await Task.Run(() =>
        {
            consoleService.Output($"[{(schemas.Count > 0 ? "{" : "")}");
            schemas.ForEach(schema =>
            {
                consoleService.Output($"\t\"name\": \"{schema.Name}\",");
                consoleService.Output($"\t\"status\": \"{schema.Status}\"");
                if (schemas.FindIndex(_ => _ == schema) < schemas.Count - 1)
                {
                    consoleService.Output("}, {");
                }
            });
            consoleService.Output($"{(schemas.Count > 0 ? "}" : "")}]");
        });

        return EExecuteResult.Succeeded;
    }

    // Behalte die synchrone Methode für Abwärtskompatibilität
    public EExecuteResult List(ICommandOptions options)
    {
        return ListAsync(options).GetAwaiter().GetResult();
    }

    private int FindIndexByName(string schemaName)
    {
        var schemaList = configFile.Config.Schema;
        return schemaList.FindIndex(schema => schema.Name.Equals(schemaName));
    }
}

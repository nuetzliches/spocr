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
    IReportService reportService
)
{
    public async Task<ExecuteResultEnum> UpdateAsync(ISchemaUpdateCommandOptions options)
    {
        var schemaName = options.SchemaName;
        var schemaIndex = FindIndexByName(schemaName);

        if (schemaIndex < 0)
        {
            reportService.Error($"Cant find schema '{schemaName}'");
            return ExecuteResultEnum.Error;
        }

        var status = options.Status;
        if (!string.IsNullOrEmpty(status))
        {
            configFile.Config.Schema[schemaIndex].Status = Enum.Parse<SchemaStatusEnum>(status);
        }

        await Task.Run(() => configFile.Save(configFile.Config));

        reportService.Output($"Schema '{schemaName}' updated.");
        return ExecuteResultEnum.Succeeded;
    }

    // Behalte die synchrone Methode für Abwärtskompatibilität
    public ExecuteResultEnum Update(ISchemaUpdateCommandOptions options)
    {
        return UpdateAsync(options).GetAwaiter().GetResult();
    }

    public async Task<ExecuteResultEnum> ListAsync(ICommandOptions options)
    {
        var schemas = configFile.Config?.Schema;

        if (!options.Silent && !(schemas?.Any() ?? false))
        {
            reportService.Warn($"No Schemas found");
            return ExecuteResultEnum.Aborted;
        }

        await Task.Run(() =>
        {
            reportService.Output($"[{(schemas.Count > 0 ? "{" : "")}");
            schemas.ForEach(schema =>
            {
                reportService.Output($"\t\"name\": \"{schema.Name}\",");
                reportService.Output($"\t\"status\": \"{schema.Status}\"");
                if (schemas.FindIndex(_ => _ == schema) < schemas.Count - 1)
                {
                    reportService.Output("}, {");
                }
            });
            reportService.Output($"{(schemas.Count > 0 ? "}" : "")}]");
        });

        return ExecuteResultEnum.Succeeded;
    }

    // Behalte die synchrone Methode für Abwärtskompatibilität
    public ExecuteResultEnum List(ICommandOptions options)
    {
        return ListAsync(options).GetAwaiter().GetResult();
    }

    private int FindIndexByName(string schemaName)
    {
        var schemaList = configFile.Config.Schema;
        return schemaList.FindIndex(schema => schema.Name.Equals(schemaName));
    }
}

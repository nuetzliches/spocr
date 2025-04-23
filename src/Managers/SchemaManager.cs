using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext;
using SpocR.DataContext.Queries;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SchemaManager(
    DbContext dbContext,
    IReportService reportService
)
{
    public async Task<List<SchemaModel>> ListAsync(ConfigurationModel config, CancellationToken cancellationToken = default)
    {
        var dbSchemas = await dbContext.SchemaListAsync(cancellationToken);
        if (dbSchemas == null)
        {
            return null;
        }

        var schemas = dbSchemas?.Select(i => new SchemaModel(i)).ToList();

        // overwrite with current config
        if (config?.Schema != null)
        {
            foreach (var schema in schemas)
            {
                // ! Do not compare with Id. The Id is different for each SQL-Server Instance
                var currentSchema = config.Schema.SingleOrDefault(i => i.Name == schema.Name);
                // TODO define a global and local Property "onNewSchemaFound" (IGNORE, BUILD, WARN, PROMPT) to set the default Status
                schema.Status = (currentSchema != null)
                    ? currentSchema.Status
                    : config.Project.DefaultSchemaStatus;
            }
        }

        // reorder schemas, ignored at top
        schemas = schemas.OrderByDescending(schema => schema.Status).ToList();

        var schemaListString = string.Join(',', schemas.Where(i => i.Status != SchemaStatusEnum.Ignore).Select(i => $"'{i.Name}'"));
        if (string.IsNullOrEmpty(schemaListString))
        {
            reportService.Warn("No schemas found or all schemas ignored!");
            return schemas;
        }

        var storedProcedures = await dbContext.StoredProcedureListAsync(schemaListString, cancellationToken);
        var tableTypes = await dbContext.TableTypeListAsync(schemaListString, cancellationToken);

        foreach (var schema in schemas)
        {
            schema.StoredProcedures = storedProcedures.Where(i => i.SchemaName.Equals(schema.Name))?.Select(i => new StoredProcedureModel(i))?.ToList();

            if (schema.StoredProcedures == null)
                continue;

            foreach (var storedProcedure in schema.StoredProcedures)
            {
                var inputs = await dbContext.StoredProcedureInputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                storedProcedure.Input = inputs.Select(i => new StoredProcedureInputModel(i)).ToList();

                var output = await dbContext.StoredProcedureOutputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                storedProcedure.Output = output.Select(i => new StoredProcedureOutputModel(i)).ToList();
            }

            var tableTypeModels = new List<TableTypeModel>();
            foreach (var tableType in tableTypes.Where(i => i.SchemaName.Equals(schema.Name)))
            {
                var columns = await dbContext.TableTypeColumnListAsync(tableType.UserTypeId ?? -1, cancellationToken);
                var tableTypeModel = new TableTypeModel(tableType, columns);
                tableTypeModels.Add(tableTypeModel);
            }

            schema.TableTypes = tableTypeModels;
        }

        return schemas;
    }
}

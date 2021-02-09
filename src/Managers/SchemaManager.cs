using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Queries;
using SpocR.Models;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Services;

namespace SpocR.Managers
{
    public class SchemaManager : ManagerBase
    {
        private readonly IReportService _reportService;

        public SchemaManager(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _reportService = serviceProvider.GetService<IReportService>();
        }

        public async Task<List<SchemaModel>> ListAsync(bool withStoredProcedures, ConfigurationModel config, CancellationToken cancellationToken = default)
        {
            var dbSchemas = await DbContext.SchemaListAsync(cancellationToken);
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

            if (withStoredProcedures)
            {
                var schemaListString = string.Join(',', schemas.Where(i => i.Status != SchemaStatusEnum.Ignore).Select(i => $"'{i.Name}'"));
                if (string.IsNullOrEmpty(schemaListString))
                {
                    _reportService.Warn("No schemas found or all schemas ignored!");
                }
                else
                {
                    var storedProcedures = await DbContext.StoredProcedureListAsync(schemaListString, cancellationToken);

                    foreach (var schema in schemas)
                    {
                        schema.StoredProcedures = storedProcedures.Where(i => i.SchemaName.Equals(schema.Name)).Select(i => new StoredProcedureModel(i)).ToList();

                        foreach (var storedProcedure in schema.StoredProcedures)
                        {
                            var inputs = await DbContext.StoredProcedureInputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);

                            foreach (var input in inputs.Where(i => i.IsTableType).ToList())
                            {
                                input.TableTypeColumns = await DbContext.UserTableTypeColumnListAsync(input.UserTypeId ?? -1, cancellationToken);
                            }

                            storedProcedure.Input = inputs.Select(i => new StoredProcedureInputModel(i)).ToList();
                        }

                        foreach (var storedProcedure in schema.StoredProcedures)
                        {
                            var output = await DbContext.StoredProcedureOutputListAsync(storedProcedure.SchemaName, storedProcedure.Name, cancellationToken);
                            storedProcedure.Output = output.Select(i => new StoredProcedureOutputModel(i)).ToList();
                        }
                    }
                }
            }
            return schemas;
        }
    }
}
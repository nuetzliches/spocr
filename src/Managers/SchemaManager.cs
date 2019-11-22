using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Queries;
using SpocR.Models;
using SpocR.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SpocR.Managers
{
    public class SchemaManager : ManagerBase
    {
        private readonly IReportService _reportService;
        private readonly StoredProcedureManager _storedProcedureManager;

        public SchemaManager(
            IServiceProvider serviceProvider,
            StoredProcedureManager storedProcedureManager,
            IReportService reportService
        ) : base(serviceProvider)
        { 
            // _reportService = serviceProvider.GetService<IReportService>();
            _reportService = reportService;
            _storedProcedureManager = storedProcedureManager;
        }

        public async Task<List<SchemaModel>> ListAsync(ConfigurationModel config, bool withStoredProcedures = true, CancellationToken cancellationToken = default)
        {
            var dbSchemas = await DbContext.SchemaListAsync(cancellationToken);
            var schemas = dbSchemas?.Select(i => new SchemaModel(i)).ToList();

            // overwrite with current config
            if (config?.Schema != null)
            {
                foreach (var schema in schemas)
                {
                    var currentSchema = config.Schema.FirstOrDefault(i => i.Id == schema.Id);
                    // TODO define a global and local Property "onNewSchemaFound" (IGNORE, BUILD, WARN, PROMPT) to set the default Status
                    schema.Status = currentSchema != null ? currentSchema.Status : SchemaStatusEnum.Build;
                }
            }

            if (withStoredProcedures)
            {
                var schemasToPull = schemas.Where(i => i.Status != SchemaStatusEnum.Ignore).ToList();
                if (!schemasToPull.Any())
                {
                    _reportService.Warn("No schemas found or all schemas ignored!");
                }
                else
                {
                    var storedProcedures = await _storedProcedureManager.ListAsync(schemasToPull, config, cancellationToken);

                    foreach (var schema in schemas)
                    {
                        schema.StoredProcedures = storedProcedures.Where(i => i.SchemaId.Equals(schema.Id)).ToList();
                    }
                }
            }
            return schemas;
        }
    }
}
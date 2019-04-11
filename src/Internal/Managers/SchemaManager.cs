using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.Internal.DataContext.Queries;
using SpocR.Internal.Models;

namespace SpocR.Internal.Managers
{
    public class SchemaManager : ManagerBase
    {
        public SchemaManager(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
        }

        public async Task<List<SchemaModel>> ListAsync(bool withStoredProcedures, ConfigurationModel config, CancellationToken cancellationToken = default)
        {
            var dbSchemas = await DbContext.SchemaListAsync(cancellationToken);
            var schemas = dbSchemas?.Select(i => new SchemaModel(i)).ToList();
            
            // overwrite with current config
            if (config?.Schema != null)
            {
                foreach (var schema in schemas)
                {
                    var currentSchema = config.Schema.FirstOrDefault(i => i.Id == schema.Id);
                    schema.Status = currentSchema != null ? currentSchema.Status : SchemaStatusEnum.Build;
                }
            }

            if(withStoredProcedures) {
                var schemaListString = string.Join(',', schemas.Where(i => i.Status != SchemaStatusEnum.Ignore).Select(i => i.Id));
                var storedProcedures = await DbContext.StoredProcedureListAsync(schemaListString, cancellationToken);
                foreach(var schema in schemas) {
                    schema.StoredProcedures = storedProcedures.Where(i => i.SchemaId.Equals(schema.Id)).Select(i => new StoredProcedureModel(i)).ToList();
                    foreach(var storedProcedure in schema.StoredProcedures) {
                        var inputs = await DbContext.StoredProcedureInputListAsync(storedProcedure.Id, cancellationToken);
                        foreach(var input in inputs.Where(i => i.IsTableType).ToList())
                        {
                            input.TableTypeColumns = await DbContext.UserTableTypeColumnListAsync(input.UserTypeId ?? -1, cancellationToken);
                        }
                        storedProcedure.Input = inputs.Select(i => new StoredProcedureInputModel(i)).ToList();
                    }
                    foreach(var storedProcedure in schema.StoredProcedures) {
                        var output = await DbContext.StoredProcedureOutputListAsync(storedProcedure.Id, cancellationToken);
                        storedProcedure.Output = output.Select(i => new StoredProcedureOutputModel(i)).ToList();
                    }
                }
            }
            return schemas;
        }
    }
}
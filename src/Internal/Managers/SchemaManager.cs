using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.Internal.DataContext.Queries;
using SpocR.Internal.Models;

namespace SpocR.Internal.Managers
{
    internal class SchemaManager : ManagerBase
    {
        public SchemaManager(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
        }

        public async Task<List<SchemaModel>> ListAsync(bool withStoredProcedures, CancellationToken cancellationToken = default(CancellationToken))
        {
            var schemas = await DbContext.SchemaListAsync(cancellationToken);
            var result = schemas?.Select(i => new SchemaModel(i)).ToList();
            if(withStoredProcedures) {

                var schemaListString = string.Join(',', schemas.Select(i => i.Id));
                var storedProcedures = await DbContext.StoredProcedureListAsync(schemaListString, cancellationToken);
                foreach(var schema in result) {
                    schema.StoredProcedures = storedProcedures.Where(i => i.SchemaId.Equals(schema.Id)).Select(i => new StoredProcedureModel(i)).ToList();
                    foreach(var storedProcedure in schema.StoredProcedures) {
                        var input = await DbContext.StoredProcedureInputListAsync(storedProcedure.Id, cancellationToken);
                        storedProcedure.Input = input.Select(i => new StoredProcedureInputModel(i)).ToList();
                    }
                    foreach(var storedProcedure in schema.StoredProcedures) {
                        var output = await DbContext.StoredProcedureOutputListAsync(storedProcedure.Id, cancellationToken);
                        storedProcedure.Output = output.Select(i => new StoredProcedureOutputModel(i)).ToList();
                    }
                }
            }
            return result;
        }
    }
}
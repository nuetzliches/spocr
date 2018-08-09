using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.Internal.DataContext.Queries;
using SpocR.Internal.Models;

namespace SpocR.Internal.Managers
{
    internal class StoredProcedureManager : ManagerBase
    {
        public StoredProcedureManager(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {

        }

        public async Task<List<StoredProcedureModel>> ListAsync(List<SchemaModel> schemaList, CancellationToken cancellationToken = default(CancellationToken))
        {
            var schemaListString = string.Join(',', schemaList.Select(i => i.Id));
            var result = await DbContext.StoredProcedureListAsync(schemaListString, cancellationToken);
            return result?.Select(i => new StoredProcedureModel(i)).ToList();
        }

        public async Task<List<StoredProcedureOutputModel>> ListOutputAsync(int objectId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = await DbContext.StoredProcedureOutputListAsync(objectId, cancellationToken);
            return result?.Select(i => new StoredProcedureOutputModel(i)).ToList();
        }
    }
}
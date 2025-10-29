using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Data.Queries;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

internal interface IUserDefinedTypeMetadataProvider
{
    Task<IReadOnlyList<UserDefinedTypeRow>> GetUserDefinedTypesAsync(ISet<string> schemas, CancellationToken cancellationToken);
}

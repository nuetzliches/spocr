using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Data.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

internal interface ITableTypeMetadataProvider
{
    Task<IReadOnlyList<TableTypeMetadata>> GetTableTypesAsync(ISet<string> schemas, CancellationToken cancellationToken);
}

internal sealed record TableTypeMetadata(TableType TableType, IReadOnlyList<Column> Columns);

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

internal interface ITableMetadataProvider
{
    Task<IReadOnlyList<TableMetadata>> GetTablesAsync(ISet<string> schemas, CancellationToken cancellationToken);
}

internal sealed record TableMetadata(Table Table, IReadOnlyList<Column> Columns);

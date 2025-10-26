using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

public interface IDependencyMetadataProvider
{
    Task<IReadOnlyList<ProcedureDependency>> ResolveAsync(IEnumerable<ProcedureDependency> dependencies, CancellationToken cancellationToken);
    Task<ProcedureDependency?> ResolveAsync(ProcedureDependency dependency, CancellationToken cancellationToken);
}

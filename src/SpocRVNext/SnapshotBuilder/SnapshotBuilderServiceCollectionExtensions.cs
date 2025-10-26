using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext.SnapshotBuilder.Analyzers;
using SpocR.SpocRVNext.SnapshotBuilder.Cache;
using SpocR.SpocRVNext.SnapshotBuilder.Collectors;
using SpocR.SpocRVNext.SnapshotBuilder.Diagnostics;
using SpocR.SpocRVNext.SnapshotBuilder.Metadata;
using SpocR.SpocRVNext.SnapshotBuilder.Writers;

namespace SpocR.SpocRVNext.SnapshotBuilder;

public static class SnapshotBuilderServiceCollectionExtensions
{
    public static IServiceCollection AddSnapshotBuilder(this IServiceCollection services)
    {
        services.AddSingleton<IDependencyMetadataProvider, DatabaseDependencyMetadataProvider>();
        services.AddSingleton<IProcedureCollector, DatabaseProcedureCollector>();
        services.AddSingleton<IProcedureAnalyzer, DatabaseProcedureAnalyzer>();
        services.AddSingleton<ISnapshotWriter, ExpandedSnapshotWriter>();
        services.AddSingleton<ISnapshotCache, FileSnapshotCache>();
        services.AddSingleton<ISnapshotDiagnostics, NullSnapshotDiagnostics>();
        services.AddSingleton<SnapshotBuildOrchestrator>();
        return services;
    }
}

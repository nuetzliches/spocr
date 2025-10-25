using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext.SnapshotBuilder.Analyzers;
using SpocR.SpocRVNext.SnapshotBuilder.Cache;
using SpocR.SpocRVNext.SnapshotBuilder.Collectors;
using SpocR.SpocRVNext.SnapshotBuilder.Diagnostics;
using SpocR.SpocRVNext.SnapshotBuilder.Writers;

namespace SpocR.SpocRVNext.SnapshotBuilder;

public static class SnapshotBuilderServiceCollectionExtensions
{
    public static IServiceCollection AddSnapshotBuilder(this IServiceCollection services)
    {
        services.AddSingleton<IProcedureCollector, PlaceholderProcedureCollector>();
        services.AddSingleton<IProcedureAnalyzer, PlaceholderProcedureAnalyzer>();
        services.AddSingleton<ISnapshotWriter, PlaceholderSnapshotWriter>();
        services.AddSingleton<ISnapshotCache, NoopSnapshotCache>();
        services.AddSingleton<ISnapshotDiagnostics, NullSnapshotDiagnostics>();
        services.AddSingleton<SnapshotBuildOrchestrator>();
        return services;
    }
}

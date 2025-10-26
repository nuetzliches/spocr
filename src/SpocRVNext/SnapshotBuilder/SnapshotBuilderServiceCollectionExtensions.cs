using Microsoft.Extensions.DependencyInjection;
using SpocR.DataContext;
using SpocR.Services;
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
        services.AddSingleton<ISnapshotWriter>(provider =>
        {
            var console = provider.GetRequiredService<IConsoleService>();
            var dbContext = provider.GetRequiredService<DbContext>();
            var legacySnapshotService = provider.GetService<ISchemaSnapshotService>();
            return new ExpandedSnapshotWriter(console, dbContext, legacySnapshotService);
        });
        services.AddSingleton<ISnapshotCache, FileSnapshotCache>();
        services.AddSingleton<ISnapshotDiagnostics, ConsoleSnapshotDiagnostics>();
        services.AddSingleton<SnapshotBuildOrchestrator>();
        return services;
    }
}

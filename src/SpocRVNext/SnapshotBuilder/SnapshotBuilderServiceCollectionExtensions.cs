using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext.Data;
using SpocR.SpocRVNext.Services;
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
        services.AddSingleton<IFunctionJsonMetadataProvider, DatabaseFunctionJsonMetadataProvider>();
        services.AddSingleton<ITableMetadataProvider, DatabaseTableMetadataProvider>();
        services.AddSingleton<ITableTypeMetadataProvider, DatabaseTableTypeMetadataProvider>();
        services.AddSingleton<IUserDefinedTypeMetadataProvider, DatabaseUserDefinedTypeMetadataProvider>();
        services.AddSingleton<IProcedureModelBuilder, ProcedureModelScriptDomBuilder>();
        services.AddSingleton<IProcedureCollector, DatabaseProcedureCollector>();
        services.AddSingleton<IProcedureAnalyzer, DatabaseProcedureAnalyzer>();
        services.AddSingleton<ISnapshotWriter>(provider =>
        {
            var console = provider.GetRequiredService<IConsoleService>();
            var dbContext = provider.GetRequiredService<DbContext>();
            var legacySnapshotService = provider.GetService<ISchemaSnapshotService>();
            var tableMetadataProvider = provider.GetRequiredService<ITableMetadataProvider>();
            var tableTypeMetadataProvider = provider.GetRequiredService<ITableTypeMetadataProvider>();
            var userDefinedTypeMetadataProvider = provider.GetRequiredService<IUserDefinedTypeMetadataProvider>();
            return new ExpandedSnapshotWriter(console, dbContext, legacySnapshotService, tableMetadataProvider, tableTypeMetadataProvider, userDefinedTypeMetadataProvider);
        });
        services.AddSingleton<ISnapshotCache, FileSnapshotCache>();
        services.AddSingleton<ISnapshotDiagnostics, ConsoleSnapshotDiagnostics>();
        services.AddSingleton<SnapshotBuildOrchestrator>();
        return services;
    }
}

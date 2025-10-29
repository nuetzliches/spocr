using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpocR.AutoUpdater;
using SpocR.Commands;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Services;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Generators;
using SpocR.SpocRVNext.SnapshotBuilder;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.Utils;
using SpocRVNext.Configuration;
using SpocRVNext.Metadata;

namespace SpocR.Runtime;

/// <summary>
/// Central runtime orchestration for the vNext CLI commands (pull/build/rebuild/version).
/// Previously implemented as SpocrManager under src/Managers.
/// Consolidated here to retire the legacy manager layer.
/// </summary>
public class SpocrCliRuntime(
    SpocrService service,
    IConsoleService consoleService,
    SnapshotBuildOrchestrator snapshotBuildOrchestrator,
    SpocR.SpocRVNext.Data.DbContext vnextDbContext,
    AutoUpdaterService autoUpdaterService
)
{
    public async Task<ExecuteResultEnum> PullAsync(ICommandOptions options)
    {
        await RunAutoUpdateAsync(options);

        EnvConfiguration envConfig;
        try
        {
            var workingDirectory = DirectoryUtils.GetWorkingDirectory();
            envConfig = EnvConfiguration.Load(projectRoot: workingDirectory);
        }
        catch (Exception envEx)
        {
            consoleService.Error($"Failed to load environment configuration: {envEx.Message}");
            return ExecuteResultEnum.Error;
        }

        var connectionString = envConfig.GeneratorConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            consoleService.Error("Missing database connection string");
            consoleService.Output("\tSet SPOCR_GENERATOR_DB in your .env (or supply the value via CLI overrides).");
            return ExecuteResultEnum.Error;
        }

        vnextDbContext.SetConnectionString(connectionString);

        if (options.Verbose)
        {
            consoleService.Verbose($"[snapshot] Generator mode={envConfig.GeneratorMode}; connection length={connectionString.Length}.");
            if (options.NoCache)
            {
                consoleService.Verbose("[snapshot] Cache disabled for this run (--no-cache).");
            }
        }

        consoleService.PrintTitle("Pulling database schema with SnapshotBuilder");

        IReadOnlyList<string> schemaFilter = envConfig.BuildSchemas ?? Array.Empty<string>();
        var procedureFilter = string.IsNullOrWhiteSpace(options.Procedure) ? null : options.Procedure.Trim();

        var snapshotOptions = new SnapshotBuildOptions
        {
            Schemas = schemaFilter,
            ProcedureWildcard = string.IsNullOrWhiteSpace(procedureFilter) ? null : procedureFilter,
            NoCache = options.NoCache,
            Verbose = options.Verbose
        };

        if (snapshotOptions.Schemas.Count > 0)
        {
            consoleService.Verbose($"[snapshot] Schema filter: {string.Join(", ", snapshotOptions.Schemas)}");
        }

        if (!string.IsNullOrWhiteSpace(snapshotOptions.ProcedureWildcard))
        {
            consoleService.Verbose($"[snapshot] Procedure filter: {snapshotOptions.ProcedureWildcard}");
        }

        var stopwatch = Stopwatch.StartNew();
        SnapshotBuildResult result;
        try
        {
            result = await snapshotBuildOrchestrator.RunAsync(snapshotOptions).ConfigureAwait(false);
        }
        catch (SqlException sqlEx)
        {
            consoleService.Error($"Database error during snapshot build: {sqlEx.Message}");
            if (options.Verbose)
            {
                consoleService.Error(sqlEx.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
        catch (Exception ex)
        {
            consoleService.Error($"Snapshot builder failed: {ex.Message}");
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
        finally
        {
            stopwatch.Stop();
        }

        var selectedProcedures = result.ProceduresSelected ?? Array.Empty<ProcedureDescriptor>();
        var groupedBySchema = selectedProcedures
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Schema) ? "(unknown)" : p.Schema, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groupedBySchema.Count == 0)
        {
            consoleService.Warn("No stored procedures matched the configured filters.");
        }
        else
        {
            var summary = string.Join(", ", groupedBySchema.Select(g => $"{g.Key}({g.Count()})"));
            consoleService.Info($"Pulled {selectedProcedures.Count} stored procedures across {groupedBySchema.Count} schema(s): {summary}");
        }

        var collectMs = result.CollectDuration > TimeSpan.Zero ? result.CollectDuration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) : null;
        var analyzeMs = result.AnalyzeDuration > TimeSpan.Zero ? result.AnalyzeDuration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) : null;
        var writeMs = result.WriteDuration > TimeSpan.Zero ? result.WriteDuration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) : null;
        var perPhaseSummary = string.Join(", ", new[]
        {
            collectMs is null ? null : $"collect={collectMs}ms",
            analyzeMs is null ? null : $"analyze={analyzeMs}ms",
            writeMs is null ? null : $"write={writeMs}ms"
        }.Where(static segment => segment is not null));

        if (!string.IsNullOrWhiteSpace(perPhaseSummary))
        {
            consoleService.Info($"Analyzed={result.ProceduresAnalyzed} reused={result.ProceduresReused} written={result.FilesWritten} unchanged={result.FilesUnchanged} in {stopwatch.ElapsedMilliseconds} ms ({perPhaseSummary}).");
        }
        else
        {
            consoleService.Info($"Analyzed={result.ProceduresAnalyzed} reused={result.ProceduresReused} written={result.FilesWritten} unchanged={result.FilesUnchanged} in {stopwatch.ElapsedMilliseconds} ms.");
        }

        if (result.Diagnostics != null && result.Diagnostics.Count > 0)
        {
            var metricsSummary = string.Join(", ", result.Diagnostics
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key}={pair.Value}"));
            consoleService.Info($"[snapshot] metrics: {metricsSummary}");
        }

        if (options.DryRun)
        {
            consoleService.PrintDryRunMessage();
        }

        return ExecuteResultEnum.Succeeded;
    }

    public async Task<ExecuteResultEnum> BuildAsync(ICommandOptions options)
    {
        await RunAutoUpdateAsync(options);
        var workingDirectory = DirectoryUtils.GetWorkingDirectory();
        EnvConfiguration envConfig;
        try
        {
            envConfig = EnvConfiguration.Load(projectRoot: workingDirectory);
        }
        catch (Exception envEx)
        {
            consoleService.Error($"Failed to load environment configuration: {envEx.Message}");
            return ExecuteResultEnum.Error;
        }

        if (!await EnsureSnapshotAsync(workingDirectory))
        {
            return ExecuteResultEnum.Error;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(envConfig.GeneratorConnectionString))
            {
                vnextDbContext.SetConnectionString(envConfig.GeneratorConnectionString);
            }

            consoleService.PrintTitle("Generating table types from snapshot (.spocr)");

            var renderer = new SimpleTemplateEngine();
            var toolRoot = Directory.GetCurrentDirectory();
            var templatesDir = Path.Combine(toolRoot, "src", "SpocRVNext", "Templates");
            ITemplateLoader? loader = Directory.Exists(templatesDir) ? new FileSystemTemplateLoader(templatesDir) : null;
            var metadata = new TableTypeMetadataProvider(workingDirectory);
            var generator = new TableTypesGenerator(envConfig, metadata, renderer, loader, workingDirectory);
            var written = generator.Generate();

            var outputDir = string.IsNullOrWhiteSpace(envConfig.OutputDir) ? "SpocR" : envConfig.OutputDir;
            if (options.Verbose)
            {
                consoleService.Verbose($"[build] TableTypes output root: {Path.Combine(workingDirectory, outputDir)}");
            }
            consoleService.Output($"Generated {written} table type artifact(s) into '{outputDir}'.");

            if (options.DryRun)
            {
                consoleService.PrintDryRunMessage();
            }

            return ExecuteResultEnum.Succeeded;
        }
        catch (SqlException sqlEx)
        {
            consoleService.Error($"Database error during the build process: {sqlEx.Message}");
            if (options.Verbose)
            {
                consoleService.Error(sqlEx.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
        catch (Exception ex)
        {
            consoleService.Error($"Unexpected error during the build process: {ex.Message}");
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
    }

    public async Task<ExecuteResultEnum> RemoveAsync(ICommandOptions options)
    {
        await RunAutoUpdateAsync(options);

        consoleService.Warn("remove is deprecated for the vNext CLI; delete generated files manually if required.");
        return ExecuteResultEnum.Skipped;
    }

    public async Task<ExecuteResultEnum> GetVersionAsync()
    {
        var current = service.Version;
        var latest = await autoUpdaterService.GetLatestVersionAsync();

        consoleService.Output($"Version: {current.ToVersionString()}");

        if (current.IsGreaterThan(latest))
            consoleService.Output($"Latest:  {latest?.ToVersionString()} (Development build)");
        else
            consoleService.Output($"Latest:  {latest?.ToVersionString() ?? current.ToVersionString()}");

        return ExecuteResultEnum.Succeeded;
    }

    private async Task RunAutoUpdateAsync(ICommandOptions options)
    {
        if (options.NoAutoUpdate)
        {
            consoleService.Verbose("Auto-update skipped via --no-auto-update flag");
            return;
        }

        // Environment variable guard (mirrors service internal check for early exit)
        if (Environment.GetEnvironmentVariable("SPOCR_SKIP_UPDATE")?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" ||
            Environment.GetEnvironmentVariable("SPOCR_NO_UPDATE")?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on")
        {
            consoleService.Verbose("Auto-update skipped via environment variable before invoking service");
            return;
        }

        if (!options.Quiet)
        {
            try
            {
                await autoUpdaterService.RunAsync();
            }
            catch (Exception ex)
            {
                consoleService.Warn($"Auto-update check failed: {ex.Message}");
            }
        }
    }

    private async Task<bool> EnsureSnapshotAsync(string workingDirectory)
    {
        try
        {
            var schemaDir = Path.Combine(workingDirectory, ".spocr", "schema");
            if (!Directory.Exists(schemaDir) || Directory.GetFiles(schemaDir, "*.json").Length == 0)
            {
                consoleService.Error("No snapshot found. Run 'spocr pull' before 'spocr build'.");
                consoleService.Output("\tUse 'spocr rebuild' to run pull and build in a single step.");
                return false;
            }
            return true;
        }
        catch (Exception)
        {
            consoleService.Error("Unable to verify snapshot presence.");
            return false;
        }
    }
}

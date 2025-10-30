using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpocR.SpocRVNext.Cli;
using SpocR.SpocRVNext.Core;
using SpocR.SpocRVNext.Extensions;
using SpocR.SpocRVNext.Services;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Generators;
using SpocR.SpocRVNext.SnapshotBuilder;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.SpocRVNext.Utils;
using SpocR.SpocRVNext.Configuration;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.SpocRVNext.Runtime;

/// <summary>
/// Central runtime orchestration for the vNext CLI commands (pull/build/rebuild/version).
/// Previously implemented as SpocrManager under src/Managers.
/// Consolidated here to retire the legacy manager layer.
/// </summary>
public class SpocrCliRuntime(
    SpocrService service,
    IConsoleService consoleService,
    SnapshotBuildOrchestrator snapshotBuildOrchestrator,
    SpocR.SpocRVNext.Data.DbContext vnextDbContext
)
{
    private bool _legacyWarningPrinted;

    public async Task<ExecuteResultEnum> PullAsync(ICommandOptions options)
    {
        EnvConfiguration envConfig;
        try
        {
            var workingDirectory = DirectoryUtils.GetWorkingDirectory();
            envConfig = EnvConfiguration.Load(projectRoot: workingDirectory);
            WarnLegacyArtifacts(workingDirectory);
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
                consoleService.Error(sqlEx.StackTrace ?? string.Empty);
            }
            return ExecuteResultEnum.Error;
        }
        catch (Exception ex)
        {
            consoleService.Error($"Snapshot builder failed: {ex.Message}");
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace ?? string.Empty);
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
        var workingDirectory = DirectoryUtils.GetWorkingDirectory();
        EnvConfiguration envConfig;
        try
        {
            envConfig = EnvConfiguration.Load(projectRoot: workingDirectory);
            WarnLegacyArtifacts(workingDirectory);
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

            var outputDir = string.IsNullOrWhiteSpace(envConfig.OutputDir) ? "SpocR" : envConfig.OutputDir.Trim();
            if (options.Verbose)
            {
                consoleService.Verbose($"[build] TableTypes output root: {Path.Combine(workingDirectory, outputDir)}");
            }
            consoleService.Output($"Generated {written} table type artifact(s) into '{outputDir}'.");

            consoleService.PrintTitle("Generating procedure artifacts");

            IReadOnlyList<SpocR.SpocRVNext.Metadata.ProcedureDescriptor> procedures;
            try
            {
                var schemaProvider = new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(workingDirectory);
                procedures = schemaProvider.GetProcedures();
                if (options.Verbose)
                {
                    consoleService.Verbose($"[build] Procedures available: {procedures.Count}");
                }
            }
            catch (Exception ex)
            {
                consoleService.Warn($"Failed to load procedure metadata: {ex.Message}");
                procedures = Array.Empty<SpocR.SpocRVNext.Metadata.ProcedureDescriptor>();
            }

            if (procedures.Count == 0)
            {
                consoleService.Warn("No stored procedures found in snapshot metadata – skipping procedure generation.");
            }
            else
            {
                var namespaceResolver = new NamespaceResolver(envConfig, msg => consoleService.Verbose($"[proc-ns] {msg}"));
                var nsRoot = namespaceResolver.Resolve(workingDirectory);
                if (string.IsNullOrWhiteSpace(nsRoot))
                {
                    nsRoot = "SpocR";
                    consoleService.Warn("Namespace resolution returned empty value. Falling back to 'SpocR'.");
                }

                static string ResolveNamespaceSegment(string? outputSetting)
                {
                    if (string.IsNullOrWhiteSpace(outputSetting))
                    {
                        return "SpocR";
                    }

                    var trimmed = outputSetting.Trim().Trim('.', '/', '\\');
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        return "SpocR";
                    }

                    var segments = trimmed.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    var lastSegment = segments.LastOrDefault();
                    if (string.IsNullOrWhiteSpace(lastSegment))
                    {
                        return "SpocR";
                    }

                    var sanitized = new string(lastSegment.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
                    return string.IsNullOrWhiteSpace(sanitized) ? "SpocR" : sanitized;
                }

                var nsSegment = ResolveNamespaceSegment(envConfig.OutputDir);
                var finalNamespace = nsRoot.EndsWith('.' + nsSegment, StringComparison.OrdinalIgnoreCase)
                    ? nsRoot
                    : nsRoot + '.' + nsSegment;

                var procedureOutputRoot = string.IsNullOrWhiteSpace(envConfig.OutputDir)
                    ? Path.Combine(workingDirectory, "SpocR")
                    : (Path.IsPathRooted(envConfig.OutputDir)
                        ? Path.GetFullPath(envConfig.OutputDir)
                        : Path.Combine(workingDirectory, envConfig.OutputDir));

                Directory.CreateDirectory(procedureOutputRoot);

                var proceduresGenerator = new ProceduresGenerator(renderer, () => procedures, loader, workingDirectory, envConfig);
                var generatedProcedures = proceduresGenerator.Generate(finalNamespace, procedureOutputRoot);
                consoleService.Output($"Generated {generatedProcedures} procedure artifact(s) into '{outputDir}'.");
            }

            consoleService.PrintTitle("Generating DbContext artifacts");

            if (procedures.Count > 0 && options.Verbose)
            {
                consoleService.Verbose($"[build] DbContext procedures available: {procedures.Count}");
            }

            var configManager = new SpocR.SpocRVNext.Infrastructure.FileManager<SpocR.SpocRVNext.Models.ConfigurationModel>(
                service,
                Constants.ConfigurationFile,
                service.GetDefaultConfiguration());
            var dbContextOutputService = new OutputService(configManager, consoleService);
            var dbContextGenerator = new DbContextGenerator(
                configManager,
                dbContextOutputService,
                consoleService,
                renderer,
                loader,
                () => procedures);

            await dbContextGenerator.GenerateAsync(options.DryRun).ConfigureAwait(false);
            consoleService.Output("DbContext artifacts updated under 'SpocR'.");

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
                consoleService.Error(sqlEx.StackTrace ?? string.Empty);
            }
            return ExecuteResultEnum.Error;
        }
        catch (Exception ex)
        {
            consoleService.Error($"Unexpected error during the build process: {ex.Message}");
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace ?? string.Empty);
            }
            return ExecuteResultEnum.Error;
        }
    }

    public Task<ExecuteResultEnum> RemoveAsync(ICommandOptions options)
    {
        consoleService.Warn("remove is deprecated for the vNext CLI; delete generated files manually if required.");
        return Task.FromResult(ExecuteResultEnum.Skipped);
    }

    public Task<ExecuteResultEnum> GetVersionAsync()
    {
        var current = service.Version;
        consoleService.Output($"Version: {current.ToVersionString()}");
        return Task.FromResult(ExecuteResultEnum.Succeeded);
    }

    private Task<bool> EnsureSnapshotAsync(string workingDirectory)
    {
        try
        {
            var schemaDir = Path.Combine(workingDirectory, ".spocr", "schema");
            if (!Directory.Exists(schemaDir) || Directory.GetFiles(schemaDir, "*.json").Length == 0)
            {
                consoleService.Error("No snapshot found. Run 'spocr pull' before 'spocr build'.");
                consoleService.Output("\tUse 'spocr rebuild' to run pull and build in a single step.");
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            consoleService.Error("Unable to verify snapshot presence.");
            return Task.FromResult(false);
        }
    }

    private void WarnLegacyArtifacts(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || _legacyWarningPrinted)
        {
            return;
        }

        var findings = new List<string>();

        try
        {
            var legacyConfigPath = Path.Combine(workingDirectory, Constants.ConfigurationFile);
            if (File.Exists(legacyConfigPath))
            {
                findings.Add($"Found legacy config '{Constants.ConfigurationFile}' at {legacyConfigPath}. SpocR vNext ignores this file.");
            }

            foreach (var userConfig in Directory.GetFiles(workingDirectory, "spocr.user.*.json", SearchOption.TopDirectoryOnly))
            {
                findings.Add($"Found legacy user config '{Path.GetFileName(userConfig)}' at {userConfig}. Remove or archive it after migration.");
            }
        }
        catch (Exception ex)
        {
            consoleService.Verbose($"[legacy-scan] Unable to inspect legacy config files: {ex.Message}");
        }

        try
        {
            var globalPath = Path.Combine(workingDirectory, Constants.GlobalConfigurationFile);
            if (File.Exists(globalPath))
            {
                findings.Add($"Found legacy global config '{Constants.GlobalConfigurationFile}' at {globalPath}. Remove it after migration.");
            }
        }
        catch (Exception ex)
        {
            consoleService.Verbose($"[legacy-scan] Unable to inspect spocr.global.json: {ex.Message}");
        }

        try
        {
            var dataContextDir = Path.Combine(workingDirectory, "DataContext");
            if (Directory.Exists(dataContextDir))
            {
                findings.Add($"Legacy DataContext directory detected at {dataContextDir}. The vNext pipeline no longer updates this output.");
            }
        }
        catch (Exception ex)
        {
            consoleService.Verbose($"[legacy-scan] Unable to inspect DataContext directory: {ex.Message}");
        }

        if (findings.Count == 0)
        {
            return;
        }

        _legacyWarningPrinted = true;
        consoleService.Warn("Legacy artifacts detected; SpocR vNext will not maintain these anymore.");
        foreach (var note in findings)
        {
            consoleService.Warn("  - " + note);
        }
        consoleService.Warn("Clean up the legacy artifacts to complete the migration.");
    }
}

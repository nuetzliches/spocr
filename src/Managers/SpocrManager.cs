using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpocR.AutoUpdater;
using SpocR.CodeGenerators;
using SpocR.Commands;
using SpocR.Commands.Spocr;
using SpocR.DataContext;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Services;
using SpocR.Models;
using SpocR.DataContext.Queries;
using SpocR.DataContext.Models;
using SpocR.SpocRVNext.Engine; // vNext template engine
using SpocR.SpocRVNext; // dispatcher & generator
using SpocRVNext.Configuration; // EnvConfiguration
using SpocRVNext.Metadata; // vNext TableType metadata provider
using SpocR.SpocRVNext.Generators; // vNext TableTypesGenerator
using SpocR.SpocRVNext.SnapshotBuilder;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.Managers;

// Hilfs-Zeilenmodelle fÃƒÂ¼r generisches Mapping von Tabellen- und View-Namen
internal class TableNameRow
{
    public string schema_name { get; set; }
    public string table_name { get; set; }
}

internal class ViewNameRow
{
    public string schema_name { get; set; }
    public string view_name { get; set; }
}

public class SpocrManager(
    SpocrService service,
    OutputService output,
    CodeGenerationOrchestrator orchestrator,
    SpocrProjectManager projectManager,
    IConsoleService consoleService,
    SnapshotBuildOrchestrator snapshotBuildOrchestrator,
    FileManager<GlobalConfigurationModel> globalConfigFile,
    FileManager<ConfigurationModel> configFile,
    DbContext dbContext,
    AutoUpdaterService autoUpdaterService
)
{
    public async Task<ExecuteResultEnum> CreateAsync(ICreateCommandOptions options)
    {
        await RunAutoUpdateAsync(options);

        if (configFile.Exists())
        {
            consoleService.Error("Configuration already exists");
            consoleService.Output($"\tTo view current configuration, run '{Constants.Name} status'");
            return ExecuteResultEnum.Error;
        }

        if (!options.Quiet && !options.Force)
        {
            var proceed = consoleService.GetYesNo($"Create a new {Constants.ConfigurationFile} file?", true);
            if (!proceed) return ExecuteResultEnum.Aborted;
        }

        var targetFramework = options.TargetFramework;
        if (!options.Quiet)
        {
            targetFramework = consoleService.GetString("TargetFramework:", targetFramework);
        }

        var appNamespace = options.Namespace;
        if (!options.Quiet)
        {
            appNamespace = consoleService.GetString("Your Namespace:", appNamespace);
        }

        var connectionString = "";
        // Role deprecated Ã¢â‚¬â€œ only display options as migration notice if user explicitly provides a value
        var roleKindString = options.Role;
        RoleKindEnum roleKind = RoleKindEnum.Default; // Always set Default
        string libNamespace = null;
        if (!options.Quiet && !string.IsNullOrWhiteSpace(roleKindString))
        {
            if (Enum.TryParse(roleKindString, true, out RoleKindEnum parsed) && parsed != RoleKindEnum.Default)
            {
                consoleService.Warn("[deprecation] Providing a role is deprecated and ignored. Default role is always applied.");
            }
        }

        var config = service.GetDefaultConfiguration(targetFramework, appNamespace, connectionString, roleKind, libNamespace);

        if (options.DryRun)
        {
            consoleService.PrintConfiguration(config);
            consoleService.PrintDryRunMessage();
        }
        else
        {
            await configFile.SaveAsync(config);
            projectManager.Create(options);

            if (!options.Quiet)
            {
                consoleService.Output($"{Constants.ConfigurationFile} successfully created.");
            }
        }

        return ExecuteResultEnum.Succeeded;
    }

    public async Task<ExecuteResultEnum> PullAsync(ICommandOptions options)
    {
        await RunAutoUpdateAsync(options);

        ConfigurationModel? config = null;

        if (configFile.Exists())
        {
            config = await LoadAndMergeConfigurationsAsync();
            if (config == null)
            {
                consoleService.Error("Failed to read configuration file");
                return ExecuteResultEnum.Error;
            }

            var configUpdated = false;
            try
            {
                if (config.Project != null && config.Schema != null)
                {
                    if ((config.Project.IgnoredSchemas == null || config.Project.IgnoredSchemas.Count == 0) && config.Schema.Count > 0)
                    {
                        var ignored = config.Schema
                            .Where(s => s.Status == SchemaStatusEnum.Ignore)
                            .Select(s => s.Name)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (ignored.Count > 0)
                        {
                            config.Project.IgnoredSchemas = ignored;
                            consoleService.Info($"[migration] Collected {ignored.Count} ignored schema name(s) into Project.IgnoredSchemas");
                            configUpdated = true;
                        }
                    }

                    config.Schema = null;
                    configUpdated = true;
                }
            }
            catch (Exception mx)
            {
                consoleService.Verbose($"[migration-warn] {mx.Message}");
            }

            if (configUpdated)
            {
                try
                {
                    await configFile.SaveAsync(config);
                }
                catch (Exception saveEx)
                {
                    consoleService.Verbose($"[migration-warn] Failed to persist schema migration: {saveEx.Message}");
                }
            }

            if (config.Project == null)
            {
                consoleService.Error("Configuration is invalid (project node missing)");
                return ExecuteResultEnum.Error;
            }

            if (await RunConfigVersionCheckAsync(options) == ExecuteResultEnum.Aborted)
            {
                return ExecuteResultEnum.Aborted;
            }
        }

        EnvConfiguration envConfig;
        try
        {
            var workingDirectory = Utils.DirectoryUtils.GetWorkingDirectory();
            envConfig = EnvConfiguration.Load(projectRoot: workingDirectory);
        }
        catch (Exception envEx)
        {
            if (config == null)
            {
                consoleService.Error($"Failed to load environment configuration: {envEx.Message}");
                return ExecuteResultEnum.Error;
            }

            consoleService.Verbose($"[snapshot] EnvConfiguration fallback: {envEx.Message}");
            envConfig = new EnvConfiguration
            {
                GeneratorMode = "legacy",
                GeneratorConnectionString = config.Project?.DataBase?.ConnectionString,
                DefaultConnection = config.Project?.DataBase?.ConnectionString,
                BuildSchemas = Array.Empty<string>()
            };
        }

        if (!configFile.Exists())
        {
            if (string.IsNullOrWhiteSpace(envConfig.GeneratorConnectionString))
            {
                consoleService.Error("Configuration file not found");
                consoleService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
                return ExecuteResultEnum.Error;
            }

            consoleService.Warn("[bridge] spocr.json missing – proceeding using .env (SPOCR_GENERATOR_DB). Some legacy-only features unavailable.");
        }

        var connectionString = !string.IsNullOrWhiteSpace(envConfig.GeneratorConnectionString)
            ? envConfig.GeneratorConnectionString
            : config?.Project?.DataBase?.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            consoleService.Error("Missing database connection string");
            consoleService.Output($"\tAdd it to spocr.json (Project.DataBase.ConnectionString) or set SPOCR_GENERATOR_DB in your .env.");
            return ExecuteResultEnum.Error;
        }

        dbContext.SetConnectionString(connectionString);

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
        // Detect dual/next mode early (do not impact legacy build logic until after legacy generation for dual)
        var genMode = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(genMode)) genMode = "dual"; // default bridge phase behavior

        if (!configFile.Exists())
        {
            var genModeMissing = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE")?.Trim().ToLowerInvariant();
            var envConn = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_DB");
            if (genModeMissing is "dual" or "next" && !string.IsNullOrWhiteSpace(envConn))
            {
                consoleService.Warn("[bridge] spocr.json missing Ã¢â‚¬â€œ build proceeding using .env values.");
            }
            else
            {
                consoleService.Error("Configuration file not found");
                consoleService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
                return ExecuteResultEnum.Error;
            }
        }

        await RunAutoUpdateAsync(options);

        if (await RunConfigVersionCheckAsync(options) == ExecuteResultEnum.Aborted)
            return ExecuteResultEnum.Aborted;

        consoleService.PrintTitle($"Build DataContext from {Constants.ConfigurationFile}");

        // Configure AST verbose diagnostics (bind/derived logs) according to global --verbose flag
        try { SpocR.Models.StoredProcedureContentModel.SetAstVerbose(options.Verbose); } catch { }

        // Reuse unified configuration loading + user overrides
        var mergedConfig = await LoadAndMergeConfigurationsAsync();
        if (mergedConfig?.Project == null)
        {
            consoleService.Error("Configuration is invalid (project node missing)");
            return ExecuteResultEnum.Error;
        }

        // Propagate merged configuration to shared configFile so generators (TemplateManager, OutputService) see updated values like Project.Output.Namespace
        try
        {
            configFile.OverwriteWithConfig = mergedConfig;
            // Force refresh of cached Config instance
            await configFile.ReloadAsync();
        }
        catch (Exception ex)
        {
            consoleService.Warn($"Could not propagate merged configuration to FileManager: {ex.Message}");
        }

        var project = mergedConfig.Project;
        var connectionString = project?.DataBase?.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            consoleService.Error("Missing database connection string");
            consoleService.Output($"\tAdd it to {Constants.ConfigurationFile} (Project.DataBase.ConnectionString) or run '{Constants.Name} set --cs \"your-connection-string\"'.");
            return ExecuteResultEnum.Error;
        }
        if (options.Verbose)
        {
            consoleService.Verbose($"[build] Using connection string (length={connectionString?.Length}) from configuration.");
        }

        try
        {
            dbContext.SetConnectionString(connectionString);
            if (options.Verbose)
            {
                try
                {
                    var working = Utils.DirectoryUtils.GetWorkingDirectory();
                    var outputBase = Utils.DirectoryUtils.GetWorkingDirectory(project.Output?.DataContext?.Path ?? "(null)");
                    consoleService.Verbose($"[diag-build] workingDir={working} outputBase={outputBase} genMode={genMode}");
                }
                catch { }
            }
            // Ensure snapshot presence (required for metadata-driven generation) Ã¢â‚¬â€œ but still require connection now (no offline mode)
            try
            {
                var working = Utils.DirectoryUtils.GetWorkingDirectory();
                var schemaDir = System.IO.Path.Combine(working, ".spocr", "schema");
                if (!System.IO.Directory.Exists(schemaDir) || System.IO.Directory.GetFiles(schemaDir, "*.json").Length == 0)
                {
                    consoleService.Error("No snapshot found. Run 'spocr pull' before 'spocr build'.");
                    consoleService.Output($"\tAlternative: 'spocr rebuild{(string.IsNullOrWhiteSpace(options.Path) ? string.Empty : " -p " + options.Path)}' fÃƒÂ¼hrt Pull + Build in einem Schritt aus.");
                    return ExecuteResultEnum.Error;
                }
            }
            catch (Exception)
            {
                consoleService.Error("Unable to verify snapshot presence.");
                return ExecuteResultEnum.Error;
            }

            // Optional informational warning if legacy schema section still present (non-empty) Ã¢â‚¬â€œ snapshot only build.
            if (mergedConfig.Schema != null && mergedConfig.Schema.Count > 0 && options.Verbose)
            {
                consoleService.Verbose("[legacy-schema] config.Schema is ignored (snapshot-only build mode)");
            }

            var elapsed = await GenerateCodeAsync(project, options);

            // Derive generator success/failure/skipped metrics from elapsed dictionary
            // Convention: If a generator type was enabled but produced 0 ms, treat as skipped.
            // (Future: we could carry explicit status objects instead of inferring.)
            var enabledGenerators = elapsed.Keys.ToList();
            var succeeded = elapsed.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
            var skipped = elapsed.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();

            var summaryLines = new List<string>();
            foreach (var kv in elapsed)
            {
                summaryLines.Add($"{kv.Key} generated in {kv.Value} ms.");
            }

            summaryLines.Add("");
            summaryLines.Add($"Generators succeeded: {succeeded.Count}/{enabledGenerators.Count} [{string.Join(", ", succeeded)}]");
            if (skipped.Count > 0)
            {
                summaryLines.Add($"Generators skipped (0 changes): {skipped.Count} [{string.Join(", ", skipped)}]");
            }

            consoleService.PrintSummary(summaryLines, $"{Constants.Name} v{service.Version.ToVersionString()}");

            var totalElapsed = elapsed.Values.Sum();
            consoleService.PrintTotal($"Total elapsed time: {totalElapsed} ms.");


            // Invoke vNext pipeline in dual/next mode: real TableTypes generation (always-on)
            if (genMode == "dual" || genMode == "next")
            {
                try
                {
                    consoleService.PrintSubTitle($"vNext ({genMode}) Ã¢â‚¬â€œ TableTypes");
                    var targetProjectRoot = Utils.DirectoryUtils.GetWorkingDirectory(); // Zielprojekt (enthÃƒÂ¤lt .spocr)
                    var cfg = EnvConfiguration.Load(projectRoot: targetProjectRoot);
                    var renderer = new SimpleTemplateEngine();
                    // Templates leben im Tool-Repository, nicht im Zielprojekt
                    var toolRoot = System.IO.Directory.GetCurrentDirectory();
                    var templatesDir = System.IO.Path.Combine(toolRoot, "src", "SpocRVNext", "Templates");
                    ITemplateLoader? loader = System.IO.Directory.Exists(templatesDir) ? new FileSystemTemplateLoader(templatesDir) : null;
                    var metadata = new TableTypeMetadataProvider(targetProjectRoot); // Metadaten aus Zielprojekt (.spocr)
                    var generator = new TableTypesGenerator(cfg, metadata, renderer, loader, targetProjectRoot);
                    var count = generator.Generate();
                    consoleService.Gray($"[vnext] TableTypes generated: {count} artifacts (includes interface)");
                }
                catch (Exception vx)
                {
                    consoleService.Warn($"[vnext-warning] TableTypes generation failed: {vx.Message}");
                }
            }

            // (Removed) manager-level .env prefill: now handled centrally inside EnvConfiguration for consistency.

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
        if (!configFile.Exists())
        {
            consoleService.Error("Configuration file not found");
            consoleService.Output($"\tNothing to remove.");
            return ExecuteResultEnum.Error;
        }

        await RunAutoUpdateAsync(options);

        if (options.DryRun)
        {
            consoleService.PrintDryRunMessage("Would remove all generated files");
            return ExecuteResultEnum.Succeeded;
        }

        var proceed1 = consoleService.GetYesNo("Remove all generated files?", true);
        if (!proceed1) return ExecuteResultEnum.Aborted;

        try
        {
            output.RemoveGeneratedFiles(configFile.Config.Project.Output.DataContext.Path, options.DryRun);
            consoleService.Output("Generated folder and files removed.");
        }
        catch (Exception ex)
        {
            consoleService.Error($"Failed to remove files: {ex.Message}");
            return ExecuteResultEnum.Error;
        }

        var proceed2 = consoleService.GetYesNo($"Remove {Constants.ConfigurationFile}?", true);
        if (!proceed2) return ExecuteResultEnum.Aborted;

        await configFile.RemoveAsync(options.DryRun);
        consoleService.Output($"{Constants.ConfigurationFile} removed.");

        return ExecuteResultEnum.Succeeded;
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

    public async Task ReloadConfigurationAsync()
    {
        await configFile.ReloadAsync();
    }

    private bool _versionCheckPerformed = false;
    private async Task<ExecuteResultEnum> RunConfigVersionCheckAsync(ICommandOptions options)
    {
        if (options.NoVersionCheck || _versionCheckPerformed) return ExecuteResultEnum.Skipped;
        _versionCheckPerformed = true;

        var check = configFile.CheckVersion();
        if (!check.DoesMatch)
        {
            if (check.SpocRVersion.IsGreaterThan(check.ConfigVersion))
            {
                consoleService.Warn($"Your local {Constants.ConfigurationFile} Version {check.SpocRVersion.ToVersionString()} is greater than the {Constants.ConfigurationFile} Version {check.ConfigVersion.ToVersionString()}");
                var answer = consoleService.GetSelectionMultiline("Do you want to continue?", ["Continue", "Cancel"]);
                if (answer.Value != "Continue")
                {
                    return ExecuteResultEnum.Aborted;
                }
            }
            else if (check.SpocRVersion.IsLessThan(check.ConfigVersion))
            {
                consoleService.Warn($"Your local {Constants.ConfigurationFile} Version {check.SpocRVersion.ToVersionString()} is lower than the {Constants.ConfigurationFile} Version {check.ConfigVersion.ToVersionString()}");
                var latestVersion = await autoUpdaterService.GetLatestVersionAsync();
                var answer = consoleService.GetSelectionMultiline("Do you want to continue?", ["Continue", "Cancel", $"Update {Constants.Name} to {latestVersion}"]);
                switch (answer.Value)
                {
                    case "Update":
                        autoUpdaterService.InstallUpdate();
                        break;
                    case "Continue":
                        break;
                    default:
                        return ExecuteResultEnum.Aborted;
                }
            }
        }

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

    private Task<Dictionary<string, long>> GenerateCodeAsync(ProjectModel project, ICommandOptions options)
    {
        try
        {
            if (options is IBuildCommandOptions buildOptions && buildOptions.GeneratorTypes != GeneratorTypes.All)
            {
                orchestrator.EnabledGeneratorTypes = buildOptions.GeneratorTypes;
                consoleService.Verbose($"Generator types restricted to: {buildOptions.GeneratorTypes}");
            }

            // Access still required until full removal in v5 Ã¢â‚¬â€œ locally suppress obsolete warning
#pragma warning disable CS0618
            return orchestrator.GenerateCodeWithProgressAsync(options.DryRun, project.Role.Kind, project.Output);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace);
            }
            else
            {
                consoleService.Output("\tRun with --verbose for more details");
            }

            return Task.FromResult(new Dictionary<string, long>());
        }
    }

    private async Task<ConfigurationModel> LoadAndMergeConfigurationsAsync()
    {
        var config = await configFile.ReadAsync();
        if (config == null)
        {
            consoleService.Error("Failed to read configuration file");
            return new ConfigurationModel();
        }

        // Migration / normalization for Role (deprecation path)
        try
        {
            // If Role missing => fill with default (Kind=Default)
            if (config.Project != null && config.Project.Role == null)
            {
                config.Project.Role = new RoleModel();
            }
            // Deprecation warning when a non-default value is set
            // Deprecation warning only if old value (Lib/Extension) is used
#pragma warning disable CS0618
            if (config.Project?.Role?.Kind is SpocR.Enums.RoleKindEnum.Lib or SpocR.Enums.RoleKindEnum.Extension)
#pragma warning restore CS0618
            {
                consoleService.Warn("[deprecation] Project.Role.Kind is deprecated and will be removed in v5. Remove the 'Role' section or set it to Default. See migration guide.");
            }
        }
        catch (Exception ex)
        {
            consoleService.Verbose($"[role-migration-warn] {ex.Message}");
        }

        var userConfigFileName = Constants.UserConfigurationFile.Replace("{userId}", globalConfigFile.Config?.UserId);
        if (string.IsNullOrEmpty(globalConfigFile.Config?.UserId))
        {
            consoleService.Verbose("No user ID found in global configuration");
            return config;
        }

        var userConfigFile = new FileManager<ConfigurationModel>(service, userConfigFileName);

        if (userConfigFile.Exists())
        {
            consoleService.Verbose($"Merging user configuration from {userConfigFileName}");
            var userConfig = await userConfigFile.ReadAsync();
            if (userConfig != null)
            {
                return config.OverwriteWith(userConfig);
            }
            else
            {
                consoleService.Warn($"User configuration file exists but could not be read: {userConfigFileName}");
            }
        }

        return config;
    }
}

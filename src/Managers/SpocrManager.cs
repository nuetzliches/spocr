using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SpocrManager(
    SpocrService service,
    OutputService output,
    CodeGenerationOrchestrator orchestrator,
    SpocrProjectManager projectManager,
    IConsoleService consoleService,
    SchemaManager schemaManager,
    FileManager<GlobalConfigurationModel> globalConfigFile,
    FileManager<ConfigurationModel> configFile,
    DbContext dbContext,
    AutoUpdaterService autoUpdaterService
)
{
    public async Task<EExecuteResult> CreateAsync(ICreateCommandOptions options)
    {
        await RunAutoUpdateAsync(options);

        if (configFile.Exists())
        {
            consoleService.Error("Configuration already exists");
            consoleService.Output($"\tTo view current configuration, run '{Constants.Name} status'");
            return EExecuteResult.Error;
        }

        if (!options.Quiet && !options.Force)
        {
            var proceed = consoleService.GetYesNo($"Create a new {Constants.ConfigurationFile} file?", true);
            if (!proceed) return EExecuteResult.Aborted;
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
        var roleKindString = options.Role;
        if (!options.Quiet)
        {
            roleKindString = consoleService.GetString($"{Constants.Name} Role [Default, Lib, Extension]:", "Default");
        }

        Enum.TryParse(roleKindString, true, out ERoleKind roleKind);

        var libNamespace = options.LibNamespace;
        if (!options.Quiet)
        {
            libNamespace = roleKind == ERoleKind.Extension
                    ? consoleService.GetString($"{Constants.Name} Lib Namespace:", "Nuts.DbContext")
                    : null;
        }

        var config = service.GetDefaultConfiguration(targetFramework, appNamespace, connectionString, roleKind, libNamespace);

        if (options.DryRun)
        {
            consoleService.PrintConfiguration(config);
            consoleService.PrintDryRunMessage();
        }
        else
        {
            configFile.Save(config);
            projectManager.Create(options);

            if (!options.Quiet)
            {
                consoleService.Output($"{Constants.ConfigurationFile} successfully created.");
            }
        }

        return EExecuteResult.Succeeded;
    }

    public async Task<EExecuteResult> PullAsync(ICommandOptions options)
    {
        await RunAutoUpdateAsync(options);

        if (!configFile.Exists())
        {
            consoleService.Error("Configuration file not found");
            consoleService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
            return EExecuteResult.Error;
        }

        var config = LoadAndMergeConfigurations();

        if (await RunConfigVersionCheckAsync(options) == EExecuteResult.Aborted)
            return EExecuteResult.Aborted;

        if (string.IsNullOrWhiteSpace(config?.Project?.DataBase?.ConnectionString))
        {
            consoleService.Error("Missing database connection string");
            consoleService.Output($"\tTo configure database access, run '{Constants.Name} set --cs \"your-connection-string\"'");
            return EExecuteResult.Error;
        }

        dbContext.SetConnectionString(config.Project.DataBase.ConnectionString);
        consoleService.PrintTitle("Pulling database schema from database");

        var configSchemas = config?.Schema ?? [];

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            var schemas = await schemaManager.ListAsync(config);

            if (schemas == null || schemas.Count == 0)
            {
                consoleService.Error("No database schemas retrieved");
                consoleService.Output("\tPlease check your database connection and permissions");
                return EExecuteResult.Error;
            }

            var overwriteWithCurrentConfig = configSchemas.Count != 0;
            if (overwriteWithCurrentConfig)
            {
                foreach (var schema in schemas)
                {
                    var currentSchema = configSchemas.SingleOrDefault(i => i.Name == schema.Name);
                    schema.Status = currentSchema != null
                        ? currentSchema.Status
                        : config.Project.DefaultSchemaStatus;
                }
            }
            configSchemas = schemas;
        }
        catch (SqlException sqlEx)
        {
            consoleService.Error($"Datenbankfehler beim Abrufen der Schemas: {sqlEx.Message}");
            if (options.Verbose)
            {
                consoleService.Error(sqlEx.StackTrace);
            }
            return EExecuteResult.Error;
        }
        catch (Exception ex)
        {
            consoleService.Error($"Fehler beim Abrufen der Schemas: {ex.Message}");
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace);
            }
            return EExecuteResult.Error;
        }

        var pullSchemas = configSchemas.Where(x => x.Status == SchemaStatusEnum.Build);
        var ignoreSchemas = configSchemas.Where(x => x.Status == SchemaStatusEnum.Ignore);

        var pulledStoredProcedures = pullSchemas.SelectMany(x => x.StoredProcedures ?? []).ToList();

        var pulledSchemasWithStoredProcedures = pullSchemas
            .Select(x => new
            {
                Schema = x,
                StoredProcedures = x.StoredProcedures?.ToList()
            }).ToList();

        pulledSchemasWithStoredProcedures.ForEach(schema =>
        {
            schema.StoredProcedures?.ForEach(sp => consoleService.Verbose($"PULL: [{schema.Schema.Name}].[{sp.Name}]"));
        });
        consoleService.Output("");

        var ignoreSchemasCount = ignoreSchemas.Count();
        if (ignoreSchemasCount > 0)
        {
            consoleService.Warn($"Ignored {ignoreSchemasCount} Schemas [{string.Join(", ", ignoreSchemas.Select(x => x.Name))}]");
            consoleService.Output("");
        }

        consoleService.Info($"Pulled {pulledStoredProcedures.Count} StoredProcedures from {pullSchemas.Count()} Schemas [{string.Join(", ", pullSchemas.Select(x => x.Name))}] in {stopwatch.ElapsedMilliseconds} ms.");
        consoleService.Output("");

        if (options.DryRun)
        {
            consoleService.PrintDryRunMessage();
        }
        else
        {
            config.Schema = configSchemas;
            configFile.Save(config);
        }

        return EExecuteResult.Succeeded;
    }

    public async Task<EExecuteResult> BuildAsync(ICommandOptions options)
    {
        if (!configFile.Exists())
        {
            consoleService.Error("Configuration file not found");
            consoleService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
            return EExecuteResult.Error;
        }

        await RunAutoUpdateAsync(options);

        if (await RunConfigVersionCheckAsync(options) == EExecuteResult.Aborted)
            return EExecuteResult.Aborted;

        consoleService.PrintTitle($"Build DataContext from {Constants.ConfigurationFile}");

        var config = configFile.Config;
        if (config == null)
        {
            consoleService.Error("Configuration is invalid");
            return EExecuteResult.Error;
        }

        var project = config.Project;
        var schemas = config.Schema;
        var connectionString = project?.DataBase?.ConnectionString;

        var hasSchemas = schemas?.Count > 0;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            consoleService.Error("Missing database connection string");
            consoleService.Output($"\tTo configure database access, run '{Constants.Name} set --cs \"your-connection-string\"'");
            return EExecuteResult.Error;
        }

        try
        {
            dbContext.SetConnectionString(connectionString);

            if (!hasSchemas)
            {
                consoleService.Error("Schema information is missing");
                consoleService.Output($"\tTo retrieve database schemas, run '{Constants.Name} pull'");
                return EExecuteResult.Error;
            }

            var elapsed = GenerateCode(project, options);

            var summary = elapsed.Select(_ => $"{_.Key} generated in {_.Value} ms.");

            consoleService.PrintSummary(summary, $"{Constants.Name} v{service.Version.ToVersionString()}");

            var totalElapsed = elapsed.Values.Sum();
            consoleService.PrintTotal($"Total elapsed time: {totalElapsed} ms.");

            if (options.DryRun)
            {
                consoleService.PrintDryRunMessage();
            }

            return EExecuteResult.Succeeded;
        }
        catch (SqlException sqlEx)
        {
            consoleService.Error($"Datenbankfehler während des Build-Vorgangs: {sqlEx.Message}");
            if (options.Verbose)
            {
                consoleService.Error(sqlEx.StackTrace);
            }
            return EExecuteResult.Error;
        }
        catch (Exception ex)
        {
            consoleService.Error($"Unerwarteter Fehler während des Build-Vorgangs: {ex.Message}");
            if (options.Verbose)
            {
                consoleService.Error(ex.StackTrace);
            }
            return EExecuteResult.Error;
        }
    }

    public async Task<EExecuteResult> RemoveAsync(ICommandOptions options)
    {
        if (!configFile.Exists())
        {
            consoleService.Error("Configuration file not found");
            consoleService.Output($"\tNothing to remove.");
            return EExecuteResult.Error;
        }

        await RunAutoUpdateAsync(options);

        if (options.DryRun)
        {
            consoleService.PrintDryRunMessage("Would remove all generated files");
            return EExecuteResult.Succeeded;
        }

        var proceed1 = consoleService.GetYesNo("Remove all generated files?", true);
        if (!proceed1) return EExecuteResult.Aborted;

        try
        {
            output.RemoveGeneratedFiles(configFile.Config.Project.Output.DataContext.Path, options.DryRun);
            consoleService.Output("Generated folder and files removed.");
        }
        catch (Exception ex)
        {
            consoleService.Error($"Failed to remove files: {ex.Message}");
            return EExecuteResult.Error;
        }

        var proceed2 = consoleService.GetYesNo($"Remove {Constants.ConfigurationFile}?", true);
        if (!proceed2) return EExecuteResult.Aborted;

        configFile.Remove(options.DryRun);
        consoleService.Output($"{Constants.ConfigurationFile} removed.");

        return EExecuteResult.Succeeded;
    }

    public async Task<EExecuteResult> GetVersionAsync()
    {
        var current = service.Version;
        var latest = await autoUpdaterService.GetLatestVersionAsync();

        consoleService.Output($"Version: {current.ToVersionString()}");

        if (current.IsGreaterThan(latest))
            consoleService.Output($"Latest:  {latest?.ToVersionString()} (Development build)");
        else
            consoleService.Output($"Latest:  {latest?.ToVersionString() ?? current.ToVersionString()}");

        return EExecuteResult.Succeeded;
    }

    private bool _versionCheckPerformed = false;
    private async Task<EExecuteResult> RunConfigVersionCheckAsync(ICommandOptions options)
    {
        if (options.NoVersionCheck || _versionCheckPerformed) return EExecuteResult.Skipped;
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
                    return EExecuteResult.Aborted;
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
                        return EExecuteResult.Aborted;
                }
            }
        }

        return EExecuteResult.Succeeded;
    }

    private async Task RunAutoUpdateAsync(ICommandOptions options)
    {
        if (!options.Quiet && !options.NoAutoUpdate)
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

    private Dictionary<string, long> GenerateCode(ProjectModel project, ICommandOptions options)
    {
        try
        {
            if (options is IBuildCommandOptions buildOptions && buildOptions.GeneratorTypes != GeneratorTypes.All)
            {
                orchestrator.EnabledGeneratorTypes = buildOptions.GeneratorTypes;
                consoleService.Verbose($"Generator-Typen eingeschränkt auf: {buildOptions.GeneratorTypes}");
            }

            return orchestrator.GenerateCodeWithProgress(options.DryRun, project.Role.Kind, project.Output);
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

            return [];
        }
    }

    private ConfigurationModel LoadAndMergeConfigurations()
    {
        var config = configFile.Read();
        if (config == null)
        {
            consoleService.Error("Failed to read configuration file");
            return new ConfigurationModel();
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
            var userConfig = userConfigFile.Read();
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

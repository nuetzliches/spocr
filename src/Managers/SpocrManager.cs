using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
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
using SpocR.Utils;

namespace SpocR.Managers;

public class SpocrManager(
    SpocrService service,
    OutputService output,
    CodeGenerationOrchestrator orchestrator,
    SpocrProjectManager projectManager,
    IReportService reportService,
    SchemaManager schemaManager,
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
            reportService.Error("Configuration already exists");
            reportService.Output($"\tTo view current configuration, run '{Constants.Name} status'");
            return ExecuteResultEnum.Error;
        }

        if (!options.Silent && !options.Force)
        {
            var proceed = Prompt.GetYesNo($"Create a new {Constants.ConfigurationFile} file?", true);
            if (!proceed) return ExecuteResultEnum.Aborted;
        }

        var targetFramework = options.TargetFramework;
        if (!options.Silent)
        {
            targetFramework = Prompt.GetString("TargetFramework:", targetFramework);
        }

        var appNamespace = options.Namespace;
        if (!options.Silent)
        {
            appNamespace = Prompt.GetString("Your Namespace:", appNamespace);
        }

        var connectionString = "";
        var roleKindString = options.Role;
        if (!options.Silent)
        {
            roleKindString = Prompt.GetString($"{Constants.Name} Role [Default, Lib, Extension]:", "Default");
        }

        Enum.TryParse(roleKindString, true, out ERoleKind roleKind);

        var libNamespace = options.LibNamespace;
        if (!options.Silent)
        {
            libNamespace = roleKind == ERoleKind.Extension
                    ? Prompt.GetString($"{Constants.Name} Lib Namespace:", "Nuts.DbContext")
                    : null;
        }

        var config = service.GetDefaultConfiguration(targetFramework, appNamespace, connectionString, roleKind, libNamespace);

        if (options.DryRun)
        {
            reportService.PrintConfiguration(config);
            reportService.PrintDryRunMessage();
        }
        else
        {
            configFile.Save(config);
            projectManager.Create(options);

            if (!options.Silent)
            {
                reportService.Output($"{Constants.ConfigurationFile} successfully created.");
            }
        }

        return ExecuteResultEnum.Succeeded;
    }

    public async Task<ExecuteResultEnum> PullAsync(ICommandOptions options)
    {
        await RunAutoUpdateAsync(options);

        if (!configFile.Exists())
        {
            reportService.Error("Configuration file not found");
            reportService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
            return ExecuteResultEnum.Error;
        }

        var config = LoadAndMergeConfigurations();

        if (await RunConfigVersionCheckAsync(options) == ExecuteResultEnum.Aborted)
            return ExecuteResultEnum.Aborted;

        if (string.IsNullOrWhiteSpace(config?.Project?.DataBase?.ConnectionString))
        {
            reportService.Error("Missing database connection string");
            reportService.Output($"\tTo configure database access, run '{Constants.Name} set --cs \"your-connection-string\"'");
            return ExecuteResultEnum.Error;
        }

        dbContext.SetConnectionString(config.Project.DataBase.ConnectionString);
        reportService.PrintTitle("Pulling database schema from database");

        var configSchemas = config?.Schema ?? [];

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            var schemas = await schemaManager.ListAsync(config);

            if (schemas == null || schemas.Count == 0)
            {
                reportService.Error("No database schemas retrieved");
                reportService.Output("\tPlease check your database connection and permissions");
                return ExecuteResultEnum.Error;
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
            reportService.Error($"Datenbankfehler beim Abrufen der Schemas: {sqlEx.Message}");
            if (options.Verbose)
            {
                reportService.Error(sqlEx.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
        catch (Exception ex)
        {
            reportService.Error($"Fehler beim Abrufen der Schemas: {ex.Message}");
            if (options.Verbose)
            {
                reportService.Error(ex.StackTrace);
            }
            return ExecuteResultEnum.Error;
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
            schema.StoredProcedures?.ForEach(sp => reportService.Verbose($"PULL: [{schema.Schema.Name}].[{sp.Name}]"));
        });
        reportService.Output("");

        var ignoreSchemasCount = ignoreSchemas.Count();
        if (ignoreSchemasCount > 0)
        {
            reportService.Warn($"Ignored {ignoreSchemasCount} Schemas [{string.Join(", ", ignoreSchemas.Select(x => x.Name))}]");
            reportService.Output("");
        }

        reportService.Info($"Pulled {pulledStoredProcedures.Count} StoredProcedures from {pullSchemas.Count()} Schemas [{string.Join(", ", pullSchemas.Select(x => x.Name))}] in {stopwatch.ElapsedMilliseconds} ms.");
        reportService.Output("");

        if (options.DryRun)
        {
            reportService.PrintDryRunMessage();
        }
        else
        {
            config.Schema = configSchemas;
            configFile.Save(config);
        }

        return ExecuteResultEnum.Succeeded;
    }

    public async Task<ExecuteResultEnum> BuildAsync(ICommandOptions options)
    {
        if (!configFile.Exists())
        {
            reportService.Error("Configuration file not found");
            reportService.Output($"\tTo create a configuration file, run '{Constants.Name} create'");
            return ExecuteResultEnum.Error;
        }

        await RunAutoUpdateAsync(options);

        if (await RunConfigVersionCheckAsync(options) == ExecuteResultEnum.Aborted)
            return ExecuteResultEnum.Aborted;

        reportService.PrintTitle($"Build DataContext from {Constants.ConfigurationFile}");

        var config = configFile.Config;
        if (config == null)
        {
            reportService.Error("Configuration is invalid");
            return ExecuteResultEnum.Error;
        }

        var project = config.Project;
        var schemas = config.Schema;
        var connectionString = project?.DataBase?.ConnectionString;

        var hasSchemas = schemas?.Count > 0;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            reportService.Error("Missing database connection string");
            reportService.Output($"\tTo configure database access, run '{Constants.Name} set --cs \"your-connection-string\"'");
            return ExecuteResultEnum.Error;
        }

        try
        {
            dbContext.SetConnectionString(connectionString);

            if (!hasSchemas)
            {
                reportService.Error("Schema information is missing");
                reportService.Output($"\tTo retrieve database schemas, run '{Constants.Name} pull'");
                return ExecuteResultEnum.Error;
            }

            var elapsed = GenerateCode(project, options);

            var summary = elapsed.Select(_ => $"{_.Key} generated in {_.Value} ms.");

            reportService.PrintSummary(summary, $"{Constants.Name} v{service.Version.ToVersionString()}");

            var totalElapsed = elapsed.Values.Sum();
            reportService.PrintTotal($"Total elapsed time: {totalElapsed} ms.");

            if (options.DryRun)
            {
                reportService.PrintDryRunMessage();
            }

            return ExecuteResultEnum.Succeeded;
        }
        catch (SqlException sqlEx)
        {
            reportService.Error($"Datenbankfehler während des Build-Vorgangs: {sqlEx.Message}");
            if (options.Verbose)
            {
                reportService.Error(sqlEx.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
        catch (Exception ex)
        {
            reportService.Error($"Unerwarteter Fehler während des Build-Vorgangs: {ex.Message}");
            if (options.Verbose)
            {
                reportService.Error(ex.StackTrace);
            }
            return ExecuteResultEnum.Error;
        }
    }

    public async Task<ExecuteResultEnum> RemoveAsync(ICommandOptions options)
    {
        if (!configFile.Exists())
        {
            reportService.Error("Configuration file not found");
            reportService.Output($"\tNothing to remove.");
            return ExecuteResultEnum.Error;
        }

        await RunAutoUpdateAsync(options);

        if (options.DryRun)
        {
            reportService.PrintDryRunMessage("Would remove all generated files");
            return ExecuteResultEnum.Succeeded;
        }

        var proceed1 = Prompt.GetYesNo("Remove all generated files?", true);
        if (!proceed1) return ExecuteResultEnum.Aborted;

        try
        {
            output.RemoveGeneratedFiles(configFile.Config.Project.Output.DataContext.Path, options.DryRun);
            reportService.Output("Generated folder and files removed.");
        }
        catch (Exception ex)
        {
            reportService.Error($"Failed to remove files: {ex.Message}");
            return ExecuteResultEnum.Error;
        }

        var proceed2 = Prompt.GetYesNo($"Remove {Constants.ConfigurationFile}?", true);
        if (!proceed2) return ExecuteResultEnum.Aborted;

        configFile.Remove(options.DryRun);
        reportService.Output($"{Constants.ConfigurationFile} removed.");

        return ExecuteResultEnum.Succeeded;
    }

    public async Task<ExecuteResultEnum> GetVersionAsync()
    {
        var current = service.Version;
        var latest = await autoUpdaterService.GetLatestVersionAsync();

        reportService.Output($"Version: {current.ToVersionString()}");

        if (current.IsGreaterThan(latest))
            reportService.Output($"Latest:  {latest?.ToVersionString()} (Development build)");
        else
            reportService.Output($"Latest:  {latest?.ToVersionString() ?? current.ToVersionString()}");

        return ExecuteResultEnum.Succeeded;
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
                reportService.Warn($"Your local {Constants.ConfigurationFile} Version {check.SpocRVersion.ToVersionString()} is greater than the {Constants.ConfigurationFile} Version {check.ConfigVersion.ToVersionString()}");
                var answer = SpocrPrompt.GetSelectionMultiline("Do you want to continue?", ["Continue", "Cancel"]);
                if (answer.Value != "Continue")
                {
                    return ExecuteResultEnum.Aborted;
                }
            }
            else if (check.SpocRVersion.IsLessThan(check.ConfigVersion))
            {
                reportService.Warn($"Your local {Constants.ConfigurationFile} Version {check.SpocRVersion.ToVersionString()} is lower than the {Constants.ConfigurationFile} Version {check.ConfigVersion.ToVersionString()}");
                var latestVersion = await autoUpdaterService.GetLatestVersionAsync();
                var answer = SpocrPrompt.GetSelectionMultiline("Do you want to continue?", ["Continue", "Cancel", $"Update {Constants.Name} to {latestVersion}"]);
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
        if (!options.Silent && !options.NoAutoUpdate)
        {
            try
            {
                await autoUpdaterService.RunAsync();
            }
            catch (Exception ex)
            {
                reportService.Warn($"Auto-update check failed: {ex.Message}");
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
                reportService.Verbose($"Generator-Typen eingeschränkt auf: {buildOptions.GeneratorTypes}");
            }

            return orchestrator.GenerateCodeWithProgress(options.DryRun, project.Role.Kind, project.Output);
        }
        catch (Exception ex)
        {
            if (options.Verbose)
            {
                reportService.Error(ex.StackTrace);
            }
            else
            {
                reportService.Output("\tRun with --verbose for more details");
            }

            return [];
        }
    }

    private ConfigurationModel LoadAndMergeConfigurations()
    {
        var config = configFile.Read();
        if (config == null)
        {
            reportService.Error("Failed to read configuration file");
            return new ConfigurationModel();
        }

        var userConfigFileName = Constants.UserConfigurationFile.Replace("{userId}", globalConfigFile.Config?.UserId);
        if (string.IsNullOrEmpty(globalConfigFile.Config?.UserId))
        {
            reportService.Verbose("No user ID found in global configuration");
            return config;
        }

        var userConfigFile = new FileManager<ConfigurationModel>(service, userConfigFileName);

        if (userConfigFile.Exists())
        {
            reportService.Verbose($"Merging user configuration from {userConfigFileName}");
            var userConfig = userConfigFile.Read();
            if (userConfig != null)
            {
                return config.OverwriteWith(userConfig);
            }
            else
            {
                reportService.Warn($"User configuration file exists but could not be read: {userConfigFileName}");
            }
        }

        return config;
    }
}

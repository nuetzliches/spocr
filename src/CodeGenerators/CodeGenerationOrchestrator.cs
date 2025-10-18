using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SpocR.CodeGenerators.Models;
using SpocRVNext.Configuration; // for EnvConfiguration
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;
using SpocR.SpocRVNext.Utils; // ProjectRootResolver

namespace SpocR.CodeGenerators;

/// <summary>
/// Coordinates the different code generation processes and provides advanced configuration options
/// </summary>
/// <remarks>
/// Creates a new instance of the CodeGenerationOrchestrator
/// </remarks>
public class CodeGenerationOrchestrator(
    InputGenerator inputGenerator,
    ModelGenerator modelGenerator,
    CrudResultGenerator crudResultGenerator,
    TableTypeGenerator tableTypeGenerator,
    OutputGenerator outputGenerator,
    StoredProcedureGenerator storedProcedureGenerator,
    SpocR.SpocRVNext.Generators.DbContextGenerator dbContextGenerator,
    IConsoleService consoleService,
    OutputService outputService
)
{
    /// <summary>
    /// Indicates whether errors occurred during code generation
    /// </summary>
    public bool HasErrors { get; private set; }

    /// <summary>
    /// Generator types that should run when GenerateSelected is invoked
    /// </summary>
    public GeneratorTypes EnabledGeneratorTypes { get; set; } = GeneratorTypes.All;

    /// <summary>
    /// Executes the full code generation pipeline with detailed progress tracking and timing
    /// </summary>
    /// <param name="isDryRun">Indicates whether the generator should run in dry-run mode without writing files</param>
    /// <param name="roleKind">The project role</param>
    /// <param name="outputConfig">The output configuration</param>
    /// <returns>Dictionary with the elapsed time for each generation step</returns>
    public async Task<Dictionary<string, long>> GenerateCodeWithProgressAsync(bool isDryRun, RoleKindEnum roleKind, OutputModel outputConfig = null)
    {
        var stopwatch = new Stopwatch();
        var elapsed = new Dictionary<string, long>();
        // Steps: CodeBase (lib only), TableTypes, Inputs, Outputs, Models, StoredProcedures
        var genMode = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(genMode)) genMode = "dual"; // default
        var dbCtxEnabled = genMode is "dual" or "next";
        var totalSteps = roleKind == RoleKindEnum.Extension ? 5 : 6;
        if (dbCtxEnabled) totalSteps += 1; // additional optional step
        var currentStep = 0;

        HasErrors = false;

        try
        {
            var codeBaseAlreadyExists = roleKind == RoleKindEnum.Extension;
            if (!codeBaseAlreadyExists && outputConfig != null)
            {
                currentStep++;
                consoleService.PrintSubTitle($"Generating CodeBase (Step {currentStep}/{totalSteps})");
                stopwatch.Start();
                outputService.GenerateCodeBase(outputConfig, isDryRun);
                elapsed.Add("CodeBase", stopwatch.ElapsedMilliseconds);
            }

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating TableTypes (Step {currentStep}/{totalSteps})");
            await GenerateDataContextTableTypesAsync(isDryRun);
            elapsed.Add("TableTypes", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating Inputs (Step {currentStep}/{totalSteps})");
            await GenerateDataContextInputsAsync(isDryRun);
            elapsed.Add("Inputs", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating Outputs (Step {currentStep}/{totalSteps})");
            await GenerateDataContextOutputsAsync(isDryRun);
            elapsed.Add("Outputs", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating Output Models (Step {currentStep}/{totalSteps})");
            await GenerateDataContextModelsAsync(isDryRun);
            elapsed.Add("Models", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating StoredProcedures (Step {currentStep}/{totalSteps})");
            await GenerateDataContextStoredProceduresAsync(isDryRun);
            elapsed.Add("StoredProcedures", stopwatch.ElapsedMilliseconds);

            if (dbCtxEnabled)
            {
                currentStep++;
                stopwatch.Restart();
                consoleService.PrintSubTitle($"Generating DbContext (Step {currentStep}/{totalSteps})");
                await dbContextGenerator.GenerateAsync(isDryRun);
                elapsed.Add("DbContext", stopwatch.ElapsedMilliseconds);
            }

            // vNext extended generation (Inputs/Outputs/Results/Procedures) für das aktuell adressierte ProjektRoot (aus -p oder Arbeitsverzeichnis)
            if (dbCtxEnabled && !isDryRun)
            {
                try
                {
                    stopwatch.Restart();
                    var projectRoot = ProjectRootResolver.ResolveCurrent();
                    consoleService.PrintSubTitle($"[vNext] Generating extended artifacts for {projectRoot}");
                    var templatesDir = System.IO.Path.Combine(SpocR.Utils.DirectoryUtils.GetApplicationRoot(), "src", "SpocRVNext", "Templates");
                    SpocR.SpocRVNext.Engine.ITemplateLoader? loader = System.IO.Directory.Exists(templatesDir)
                        ? new SpocR.SpocRVNext.Engine.FileSystemTemplateLoader(templatesDir)
                        : null;
                    if (loader == null) consoleService.Warn($"[vNext] Templates-Verzeichnis fehlt: {templatesDir}");
                    var outDir = System.IO.Path.Combine(projectRoot, "SpocR");
                    var schemaProvider = new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(projectRoot);
                    var vnextGen = new SpocR.SpocRVNext.SpocRGenerator(new SpocR.SpocRVNext.Engine.SimpleTemplateEngine(), loader, schemaProviderFactory: () => schemaProvider);
                    if (!System.IO.Directory.Exists(outDir)) System.IO.Directory.CreateDirectory(outDir);
                    var cfg = EnvConfiguration.Load(projectRoot: projectRoot);
                    var count = vnextGen.GenerateAll(cfg, projectRoot);
                    consoleService.Info($"[vNext] Generated {count} unified procedure file(s).");
                    elapsed.Add("vNext", stopwatch.ElapsedMilliseconds);
                }
                catch (Exception vx)
                {
                    consoleService.Warn($"[vNext] generation skipped due to error: {vx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            consoleService.Error($"Error during code generation step {currentStep}/{totalSteps}: {ex.Message}");
            HasErrors = true;
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }

        return elapsed;
    }

    /// <summary>
    /// Runs the asynchronous generator in a blocking fashion.
    /// </summary>
    public void GenerateAll(bool isDryRun) => GenerateAllAsync(isDryRun).GetAwaiter().GetResult();

    /// <summary>
    /// Generates all available generator types
    /// </summary>
    public async Task GenerateAllAsync(bool isDryRun)
    {
        HasErrors = false;

        try
        {
            consoleService.StartProgress("Generating code...");

            await GenerateDataContextTableTypesAsync(isDryRun);
            await GenerateDataContextInputsAsync(isDryRun);
            await GenerateDataContextOutputsAsync(isDryRun);
            await GenerateDataContextModelsAsync(isDryRun);
            await GenerateDataContextStoredProceduresAsync(isDryRun);
            var genMode = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE")?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(genMode)) genMode = "dual";
            var dbCtxEnabled = genMode is "dual" or "next";
            if (dbCtxEnabled)
                await dbContextGenerator.GenerateAsync(isDryRun);

            if (dbCtxEnabled && !isDryRun)
            {
                try
                {
                    var projectRoot = ProjectRootResolver.ResolveCurrent();
                    var templatesDir = System.IO.Path.Combine(SpocR.Utils.DirectoryUtils.GetApplicationRoot(), "src", "SpocRVNext", "Templates");
                    SpocR.SpocRVNext.Engine.ITemplateLoader? loader = System.IO.Directory.Exists(templatesDir)
                        ? new SpocR.SpocRVNext.Engine.FileSystemTemplateLoader(templatesDir)
                        : null;
                    if (loader == null) consoleService.Warn($"[vNext] Templates-Verzeichnis fehlt: {templatesDir}");
                    var vnextGen = new SpocR.SpocRVNext.SpocRGenerator(new SpocR.SpocRVNext.Engine.SimpleTemplateEngine(), loader, schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(projectRoot));
                    if (!System.IO.Directory.Exists(System.IO.Path.Combine(projectRoot, "SpocR"))) System.IO.Directory.CreateDirectory(System.IO.Path.Combine(projectRoot, "SpocR"));
                    vnextGen.GenerateAll(EnvConfiguration.Load(projectRoot: projectRoot), projectRoot);
                }
                catch (Exception vx)
                {
                    consoleService.Warn($"vNext generation skipped due to error: {vx.Message}");
                }
            }

            consoleService.CompleteProgress();
        }
        catch (Exception ex)
        {
            consoleService.Error($"Error during code generation: {ex.Message}");
            HasErrors = true;
            consoleService.CompleteProgress(success: false);
            throw;
        }
    }

    /// <summary>
    /// Runs the selected generator pipeline synchronously for command handlers.
    /// </summary>
    public void GenerateSelected(bool isDryRun) => GenerateSelectedAsync(isDryRun).GetAwaiter().GetResult();

    /// <summary>
    /// Generates only the generator types defined by EnabledGeneratorTypes
    /// </summary>
    public async Task GenerateSelectedAsync(bool isDryRun)
    {
        HasErrors = false;

        try
        {
            consoleService.StartProgress("Generating selected generator types...");

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.TableTypes))
                await GenerateDataContextTableTypesAsync(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.Inputs))
                await GenerateDataContextInputsAsync(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.Outputs))
                await GenerateDataContextOutputsAsync(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.Models))
                await GenerateDataContextModelsAsync(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.StoredProcedures))
                await GenerateDataContextStoredProceduresAsync(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.DbContext))
                await dbContextGenerator.GenerateAsync(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.DbContext) && !isDryRun)
            {
                try
                {
                    var projectRoot = ProjectRootResolver.ResolveCurrent();
                    var templatesDir = System.IO.Path.Combine(SpocR.Utils.DirectoryUtils.GetApplicationRoot(), "src", "SpocRVNext", "Templates");
                    SpocR.SpocRVNext.Engine.ITemplateLoader? loader = System.IO.Directory.Exists(templatesDir)
                        ? new SpocR.SpocRVNext.Engine.FileSystemTemplateLoader(templatesDir)
                        : null;
                    if (loader == null) consoleService.Warn($"[vNext] Templates-Verzeichnis fehlt: {templatesDir}");
                    var vnextGen = new SpocR.SpocRVNext.SpocRGenerator(new SpocR.SpocRVNext.Engine.SimpleTemplateEngine(), loader, schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(projectRoot));
                    vnextGen.GenerateAll(EnvConfiguration.Load(projectRoot: projectRoot), projectRoot);
                }
                catch (Exception vx)
                {
                    consoleService.Warn($"vNext generation skipped due to error: {vx.Message}");
                }
            }

            consoleService.CompleteProgress();
        }
        catch (Exception ex)
        {
            consoleService.Error($"Error during code generation: {ex.Message}");
            HasErrors = true;
            consoleService.CompleteProgress(success: false);
        }
    }

    /// <summary>
    /// Generates TableType classes
    /// </summary>
    public Task GenerateDataContextTableTypesAsync(bool isDryRun)
    {
        return tableTypeGenerator.GenerateDataContextTableTypesAsync(isDryRun);
    }

    /// <summary>
    /// Generates input classes for stored procedures
    /// </summary>
    public Task GenerateDataContextInputsAsync(bool isDryRun)
    {
        return inputGenerator.GenerateDataContextInputs(isDryRun);
    }

    /// <summary>
    /// Generates output classes for stored procedure OUTPUT parameters
    /// </summary>
    public Task GenerateDataContextOutputsAsync(bool isDryRun)
    {
        return outputGenerator.GenerateDataContextOutputsAsync(isDryRun);
    }


    /// <summary>
    /// Generates entity model classes
    /// </summary>
    public async Task GenerateDataContextModelsAsync(bool isDryRun)
    {
        await modelGenerator.GenerateDataContextModels(isDryRun);
        await crudResultGenerator.GenerateAsync(isDryRun);
    }

    /// <summary>
    /// Generates stored procedure extension methods
    /// </summary>
    public Task GenerateDataContextStoredProceduresAsync(bool isDryRun)
    {
        return storedProcedureGenerator.GenerateDataContextStoredProceduresAsync(isDryRun);
    }
    // Entfernt: FindSampleProjectRoot – durch ProjectRootResolver ersetzt
}

/// <summary>
/// Defines the generator types that can be enabled individually
/// </summary>
[Flags]
public enum GeneratorTypes
{
    None = 0,
    TableTypes = 1,
    Inputs = 2,
    Outputs = 4,
    Models = 8,
    StoredProcedures = 16,
    DbContext = 32,
    All = TableTypes | Inputs | Outputs | Models | StoredProcedures | DbContext
}

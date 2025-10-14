using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SpocR.CodeGenerators.Models;
using SpocRVNext.Configuration; // for EnvConfiguration
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

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

            // vNext extended generation (Inputs/Outputs/Results/Procedures) when dual|next mode for sample parity observation
            if (dbCtxEnabled && !isDryRun)
            {
                try
                {
                    stopwatch.Restart();
                    consoleService.PrintSubTitle("[vNext] Generating extended artifacts (Inputs/Outputs/Results/Procedures)");
                    var templatesDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "src", "SpocRVNext", "Templates");
                    SpocR.SpocRVNext.Engine.ITemplateLoader? loader = System.IO.Directory.Exists(templatesDir)
                        ? new SpocR.SpocRVNext.Engine.FileSystemTemplateLoader(templatesDir)
                        : null;
                    var cwd = System.IO.Directory.GetCurrentDirectory();
                    // Dynamische Ermittlung des Sample-Projekt Wurzelverzeichnisses:
                    // Strategie: finde eine spocr.json unterhalb von samples/* mit einer RestApi.csproj im selben Ordner
                    string sampleRoot = FindSampleProjectRoot(cwd) ?? System.IO.Path.Combine(cwd, "samples", "restapi");
                    var sampleOutDir = System.IO.Path.Combine(sampleRoot, "SpocR");
                    var vnextGen = new SpocR.SpocRVNext.SpocRGenerator(
                        new SpocR.SpocRVNext.Engine.SimpleTemplateEngine(),
                        loader,
                        schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(sampleRoot));
                    if (!System.IO.Directory.Exists(sampleOutDir)) System.IO.Directory.CreateDirectory(sampleOutDir);
                    // Wichtig: EnvConfiguration.Load mit sampleRoot, damit NamespaceResolver die RestApi.csproj korrekt findet
                    vnextGen.GenerateAll(EnvConfiguration.Load(projectRoot: sampleRoot), sampleRoot);
                    elapsed.Add("vNext", stopwatch.ElapsedMilliseconds);
                }
                catch (Exception vx)
                {
                    consoleService.Warn($"vNext generation skipped due to error: {vx.Message}");
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
                    var templatesDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "src", "SpocRVNext", "Templates");
                    SpocR.SpocRVNext.Engine.ITemplateLoader? loader = System.IO.Directory.Exists(templatesDir)
                        ? new SpocR.SpocRVNext.Engine.FileSystemTemplateLoader(templatesDir)
                        : null;
                    var cwd2 = System.IO.Directory.GetCurrentDirectory();
                    var sampleRoot2 = FindSampleProjectRoot(cwd2) ?? System.IO.Path.Combine(cwd2, "samples", "restapi");
                    var vnextGen = new SpocR.SpocRVNext.SpocRGenerator(
                        new SpocR.SpocRVNext.Engine.SimpleTemplateEngine(),
                        loader,
                        schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(sampleRoot2));
                    if (!System.IO.Directory.Exists(System.IO.Path.Combine(sampleRoot2, "SpocR"))) System.IO.Directory.CreateDirectory(System.IO.Path.Combine(sampleRoot2, "SpocR"));
                    vnextGen.GenerateAll(EnvConfiguration.Load(projectRoot: sampleRoot2), sampleRoot2);
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
                    var templatesDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "src", "SpocRVNext", "Templates");
                    SpocR.SpocRVNext.Engine.ITemplateLoader? loader = System.IO.Directory.Exists(templatesDir)
                        ? new SpocR.SpocRVNext.Engine.FileSystemTemplateLoader(templatesDir)
                        : null;
                    var sampleRoot3 = FindSampleProjectRoot(System.IO.Directory.GetCurrentDirectory()) ?? System.IO.Directory.GetCurrentDirectory();
                    var vnextGen = new SpocR.SpocRVNext.SpocRGenerator(new SpocR.SpocRVNext.Engine.SimpleTemplateEngine(), loader, schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(sampleRoot3));
                    vnextGen.GenerateAll(EnvConfiguration.Load(projectRoot: sampleRoot3), sampleRoot3);
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
    private static string? FindSampleProjectRoot(string cwd)
    {
        try
        {
            var samplesDir = System.IO.Path.Combine(cwd, "samples");
            if (!System.IO.Directory.Exists(samplesDir)) return null;
            foreach (var dir in System.IO.Directory.EnumerateDirectories(samplesDir))
            {
                var candidateCfg = System.IO.Path.Combine(dir, "spocr.json");
                if (System.IO.File.Exists(candidateCfg))
                    return dir;
            }
        }
        catch { }
        return null;
    }
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

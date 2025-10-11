using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SpocR.CodeGenerators.Models;
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
    IConsoleService consoleService,
    OutputService outputService,
    SpocR.Managers.FileManager<ConfigurationModel> configFile
)
{
    private bool IsModernTfmOnly()
    {
        var tfm = configFile.Config?.TargetFramework;
        if (string.IsNullOrWhiteSpace(tfm) || !tfm.StartsWith("net")) return false;
        if (!int.TryParse(tfm.Substring(3).Split('.')[0], out var major)) return false;
        return major >= 10;
    }

    private bool HasCompatibility() => string.Equals(configFile.Config?.Project?.Output?.CompatibilityMode, "v4.5", StringComparison.OrdinalIgnoreCase);
    private bool UseUnifiedModern() => IsModernTfmOnly(); // always for net10+, even with compatibility
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
        var totalSteps = roleKind == RoleKindEnum.Extension ? 5 : 6;
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
            var unified = UseUnifiedModern();
            var compat = HasCompatibility();
            consoleService.PrintSubTitle($"Generating Inputs (Step {currentStep}/{totalSteps}){(unified && !compat ? " (skipped unified)" : compat && unified ? " (legacy + unified)" : string.Empty)}");
            // Legacy Inputs only when compatibility mode present
            if (compat)
            {
                await GenerateDataContextInputsAsync(isDryRun);
            }
            elapsed.Add("Inputs", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating Outputs (Step {currentStep}/{totalSteps}){(unified && !compat ? " (skipped unified)" : compat && unified ? " (legacy + unified)" : string.Empty)}");
            if (compat)
            {
                await GenerateDataContextOutputsAsync(isDryRun);
            }
            elapsed.Add("Outputs", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating Output Models (Step {currentStep}/{totalSteps}){(unified && !compat ? " (skipped unified)" : compat && unified ? " (legacy + unified)" : string.Empty)}");
            if (compat)
            {
                await GenerateDataContextModelsAsync(isDryRun);
            }
            elapsed.Add("Models", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating StoredProcedures (Step {currentStep}/{totalSteps})");
            await GenerateDataContextStoredProceduresAsync(isDryRun);
            elapsed.Add("StoredProcedures", stopwatch.ElapsedMilliseconds);
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
            var compatAll = HasCompatibility();
            // Always run legacy generators only if compatibility mode present
            if (compatAll)
            {
                await GenerateDataContextInputsAsync(isDryRun);
                await GenerateDataContextOutputsAsync(isDryRun);
                await GenerateDataContextModelsAsync(isDryRun);
            }
            await GenerateDataContextStoredProceduresAsync(isDryRun);

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

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.Inputs) && HasCompatibility())
                await GenerateDataContextInputsAsync(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.Outputs) && HasCompatibility())
                await GenerateDataContextOutputsAsync(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.Models) && HasCompatibility())
                await GenerateDataContextModelsAsync(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.StoredProcedures))
                await GenerateDataContextStoredProceduresAsync(isDryRun);

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
        // Skip CrudResult in modern layout (net10+ without compatibility mode)
        bool ModernDetector(string tfm)
        {
            if (string.IsNullOrWhiteSpace(tfm) || !tfm.StartsWith("net")) return false;
            var core = tfm.Substring(3).Split('.')[0];
            return int.TryParse(core, out var major) && major >= 10;
        }
        // We don't have direct access to the full config here; infer from OutputService namespace heuristics:
        // Approximation: if any generated versioned output root for modern TFMs would be net10+, rely on env var SPOCR_TARGET_FRAMEWORK or fallback to Constants.DefaultTargetFramework.
        var inferredTfm = configFile.Config?.TargetFramework ?? Constants.DefaultTargetFramework.ToFrameworkString();
        var compatibility = configFile.Config?.Project?.Output?.CompatibilityMode;
        var modern = ModernDetector(inferredTfm) && !string.Equals(compatibility, "v4.5", StringComparison.OrdinalIgnoreCase);
        if (!modern)
        {
            await crudResultGenerator.GenerateAsync(isDryRun);
        }
    }

    /// <summary>
    /// Generates stored procedure extension methods
    /// </summary>
    public Task GenerateDataContextStoredProceduresAsync(bool isDryRun)
    {
        return storedProcedureGenerator.GenerateDataContextStoredProceduresAsync(isDryRun);
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
    All = TableTypes | Inputs | Outputs | Models | StoredProcedures
}

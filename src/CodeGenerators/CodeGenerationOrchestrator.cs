using System;
using System.Collections.Generic;
using System.Diagnostics;
using SpocR.CodeGenerators.Models;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.CodeGenerators;

/// <summary>
/// Koordiniert die verschiedenen Code-Generator-Prozesse und bietet erweiterte Konfigurationsmöglichkeiten
/// </summary>
/// <remarks>
/// Erstellt eine neue Instanz des CodeGenerationOrchestrator
/// </remarks>
public class CodeGenerationOrchestrator(
    InputGenerator inputGenerator,
    OutputGenerator outputGenerator,
    ModelGenerator modelGenerator,
    TableTypeGenerator tableTypeGenerator,
    StoredProcedureGenerator storedProcedureGenerator,
    IConsoleService consoleService,
    OutputService outputService
)
{
    /// <summary>
    /// Gibt an, ob bei der Code-Generierung Fehler aufgetreten sind
    /// </summary>
    public bool HasErrors { get; private set; }

    /// <summary>
    /// Die Generator-Typen, die bei GenerateSelected ausgeführt werden sollen
    /// </summary>
    public GeneratorTypes EnabledGeneratorTypes { get; set; } = GeneratorTypes.All;

    /// <summary>
    /// Führt die vollständige Code-Generierung mit detaillierter Fortschrittsverfolgung und Zeiterfassung aus
    /// </summary>
    /// <param name="isDryRun">Gibt an, ob es sich um einen Testlauf ohne tatsächliche Änderungen handelt</param>
    /// <param name="roleKind">Die Rolle des Projekts</param>
    /// <param name="outputConfig">Die Output-Konfiguration</param>
    /// <returns>Dictionary mit den Ausführungszeiten der einzelnen Schritte</returns>
    public Dictionary<string, long> GenerateCodeWithProgress(bool isDryRun, ERoleKind roleKind, OutputModel outputConfig = null)
    {
        var stopwatch = new Stopwatch();
        var elapsed = new Dictionary<string, long>();
        var totalSteps = roleKind == ERoleKind.Extension ? 5 : 6;
        var currentStep = 0;

        HasErrors = false;

        try
        {
            var codeBaseAlreadyExists = roleKind == ERoleKind.Extension;
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
            GenerateDataContextTableTypes(isDryRun);
            elapsed.Add("TableTypes", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating Inputs (Step {currentStep}/{totalSteps})");
            GenerateDataContextInputs(isDryRun);
            elapsed.Add("Inputs", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating Outputs (Step {currentStep}/{totalSteps})");
            GenerateDataContextOutputs(isDryRun);
            elapsed.Add("Outputs", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating Output Models (Step {currentStep}/{totalSteps})");
            GenerateDataContextModels(isDryRun);
            elapsed.Add("Models", stopwatch.ElapsedMilliseconds);

            currentStep++;
            stopwatch.Restart();
            consoleService.PrintSubTitle($"Generating StoredProcedures (Step {currentStep}/{totalSteps})");
            GenerateDataContextStoredProcedures(isDryRun);
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
    /// Generiert alle verfügbaren Code-Typen
    /// </summary>
    public void GenerateAll(bool isDryRun)
    {
        HasErrors = false;

        try
        {
            consoleService.StartProgress("Generiere Code...");

            GenerateDataContextTableTypes(isDryRun);
            GenerateDataContextInputs(isDryRun);
            GenerateDataContextOutputs(isDryRun);
            GenerateDataContextModels(isDryRun);
            GenerateDataContextStoredProcedures(isDryRun);

            consoleService.CompleteProgress();
        }
        catch (Exception ex)
        {
            consoleService.Error($"Fehler bei der Code-Generierung: {ex.Message}");
            HasErrors = true;
            consoleService.CompleteProgress(success: false);
            throw;
        }
    }

    /// <summary>
    /// Generiert nur die ausgewählten Code-Typen basierend auf EnabledGeneratorTypes
    /// </summary>
    public void GenerateSelected(bool isDryRun)
    {
        HasErrors = false;

        try
        {
            consoleService.StartProgress("Generiere ausgewählte Code-Typen...");

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.TableTypes))
                GenerateDataContextTableTypes(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.Inputs))
                GenerateDataContextInputs(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.Outputs))
                GenerateDataContextOutputs(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.Models))
                GenerateDataContextModels(isDryRun);

            if (EnabledGeneratorTypes.HasFlag(GeneratorTypes.StoredProcedures))
                GenerateDataContextStoredProcedures(isDryRun);

            consoleService.CompleteProgress();
        }
        catch (Exception ex)
        {
            consoleService.Error($"Fehler bei der Code-Generierung: {ex.Message}");
            HasErrors = true;
            consoleService.CompleteProgress(success: false);
        }
    }

    /// <summary>
    /// Generiert TableType-Klassen
    /// </summary>
    public void GenerateDataContextTableTypes(bool isDryRun)
    {
        tableTypeGenerator.GenerateDataContextTableTypes(isDryRun);
    }

    /// <summary>
    /// Generiert Input-Klassen für Stored Procedures
    /// </summary>
    public void GenerateDataContextInputs(bool isDryRun)
    {
        inputGenerator.GenerateDataContextInputs(isDryRun);
    }

    /// <summary>
    /// Generiert Output-Klassen für Stored Procedures
    /// </summary>
    public void GenerateDataContextOutputs(bool isDryRun)
    {
        outputGenerator.GenerateDataContextOutputs(isDryRun);
    }

    /// <summary>
    /// Generiert Model-Klassen für Entitäten
    /// </summary>
    public void GenerateDataContextModels(bool isDryRun)
    {
        modelGenerator.GenerateDataContextModels(isDryRun);
    }

    /// <summary>
    /// Generiert StoredProcedure-Erweiterungsmethoden
    /// </summary>
    public void GenerateDataContextStoredProcedures(bool isDryRun)
    {
        storedProcedureGenerator.GenerateDataContextStoredProcedures(isDryRun);
    }
}

/// <summary>
/// Definiert die verschiedenen Generator-Typen, die einzeln aktiviert werden können
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
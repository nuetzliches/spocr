using System;
using System.Collections.Generic;
using SpocR.CodeGenerators.Models;
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
    IReportService reportService
)
{

    /// <summary>
    /// Gibt an, ob bei der Code-Generierung Fehler aufgetreten sind
    /// </summary>
    public bool HasErrors { get; private set; }

    /// <summary>
    /// Filtert nach spezifischen Schema-Namen, wenn gesetzt
    /// </summary>
    public List<string> SchemaFilter { get; set; }

    /// <summary>
    /// Die Generator-Typen, die bei GenerateSelected ausgeführt werden sollen
    /// </summary>
    public GeneratorTypes EnabledGeneratorTypes { get; set; } = GeneratorTypes.All;

    /// <summary>
    /// Generiert alle verfügbaren Code-Typen
    /// </summary>
    public void GenerateAll(bool isDryRun)
    {
        HasErrors = false;

        try
        {
            reportService.StartProgress("Generiere Code...");

            GenerateDataContextTableTypes(isDryRun);
            GenerateDataContextInputs(isDryRun);
            GenerateDataContextOutputs(isDryRun);
            GenerateDataContextModels(isDryRun);
            GenerateDataContextStoredProcedures(isDryRun);

            reportService.CompleteProgress();
        }
        catch (Exception ex)
        {
            reportService.Error($"Fehler bei der Code-Generierung: {ex.Message}");
            HasErrors = true;
            reportService.CompleteProgress(success: false);
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
            reportService.StartProgress("Generiere ausgewählte Code-Typen...");

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

            reportService.CompleteProgress();
        }
        catch (Exception ex)
        {
            reportService.Error($"Fehler bei der Code-Generierung: {ex.Message}");
            HasErrors = true;
            reportService.CompleteProgress(success: false);
        }
    }

    /// <summary>
    /// Generiert TableType-Klassen
    /// </summary>
    public void GenerateDataContextTableTypes(bool isDryRun)
    {
        reportService.Info("Generiere TableTypes...");
        tableTypeGenerator.GenerateDataContextTableTypes(isDryRun);
    }

    /// <summary>
    /// Generiert Input-Klassen für Stored Procedures
    /// </summary>
    public void GenerateDataContextInputs(bool isDryRun)
    {
        reportService.Info("Generiere Inputs...");
        inputGenerator.GenerateDataContextInputs(isDryRun);
    }

    /// <summary>
    /// Generiert Output-Klassen für Stored Procedures
    /// </summary>
    public void GenerateDataContextOutputs(bool isDryRun)
    {
        reportService.Info("Generiere Outputs...");
        outputGenerator.GenerateDataContextOutputs(isDryRun);
    }

    /// <summary>
    /// Generiert Model-Klassen für Entitäten
    /// </summary>
    public void GenerateDataContextModels(bool isDryRun)
    {
        reportService.Info("Generiere Models...");
        modelGenerator.GenerateDataContextModels(isDryRun);
    }

    /// <summary>
    /// Generiert StoredProcedure-Erweiterungsmethoden
    /// </summary>
    public void GenerateDataContextStoredProcedures(bool isDryRun)
    {
        reportService.Info("Generiere StoredProcedure Extensions...");
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.AutoUpdater;

/// <summary>
/// Service für das automatisierte Aktualisieren der SpocR-Anwendung
/// </summary>
public class AutoUpdaterService(
    SpocrService spocrService,
    IPackageManager packageManager,
    FileManager<GlobalConfigurationModel> globalConfigFile,
    IConsoleService consoleService
)
{
    // Konstanten für Prozentangaben im Fortschritt
    private const int InitialProgressPercentage = 10;
    private const int ProcessStartedPercentage = 20;
    private const int InstallingPackagesPercentage = 30;
    private const int RestoringPackagesPercentage = 40;
    private const int PackagesInstalledPercentage = 60;
    private const int FinalizingPercentage = 90;
    private const int CompletedPercentage = 100;

    /// <summary>
    /// Ruft die neueste verfügbare Version von SpocR ab
    /// </summary>
    public Task<Version> GetLatestVersionAsync() => packageManager.GetLatestVersionAsync();

    /// <summary>
    /// Führt die Überprüfung auf Updates durch und bietet dem Benutzer entsprechende Optionen an
    /// </summary>
    /// <param name="force">Update-Prüfung erzwingen, unabhängig von den Konfigurationseinstellungen</param>
    public async Task RunAsync(bool force = false)
    {
        if (!ShouldRunUpdate(force))
            return;

        var latestVersion = await packageManager.GetLatestVersionAsync();
        if (latestVersion == null)
        {
            consoleService.Info("Could not check for updates. Will try again later.");
            WriteShortPause();
            return;
        }

        if (ShouldOfferUpdate(latestVersion))
        {
            await OfferUpdateOptionsAsync(latestVersion);
            return;
        }

        WriteShortPause();
    }

    /// <summary>
    /// Führt die Installation des Updates durch
    /// </summary>
    public async Task InstallUpdateAsync()
    {
        CancellationTokenSource cancellationTokenSource = new();
        try
        {
            consoleService.StartProgress("Updating SpocR to the latest version");

            // Prüfen auf laufende Prozesse und diese beenden, bevor das Update ausgeführt wird
            await CloseRunningSpocrInstancesAsync();

            // Temporäres Verzeichnis für das Update erstellen
            string tempUpdateDir = CreateTempUpdateDirectory();

            // Mehrere Versuche, falls beim ersten Mal Zugriffsprobleme auftreten
            int maxRetries = 3;
            bool updateSucceeded = false;

            for (int attempt = 1; attempt <= maxRetries && !updateSucceeded; attempt++)
            {
                if (attempt > 1)
                {
                    consoleService.Info($"Retry attempt {attempt} of {maxRetries}...");
                    // Kurze Pause vor dem nächsten Versuch
                    await Task.Delay(2000);
                }

                using var updateProcess = PrepareUpdateProcess(tempUpdateDir);
                var (outputTask, errorTask) = SetupProcessOutputHandling(updateProcess, cancellationTokenSource.Token);

                // Status aktualisieren
                consoleService.UpdateProgressStatus("Preparing update process...", percentage: InitialProgressPercentage);

                // TaskCompletionSource für die Überwachung des Prozessabschlusses
                var exitEvent = new TaskCompletionSource<bool>();
                updateProcess.Exited += (_, _) => exitEvent.TrySetResult(updateProcess.ExitCode == 0);

                // Prozess starten
                consoleService.UpdateProgressStatus("Starting update process...", percentage: ProcessStartedPercentage);
                updateProcess.Start();
                updateProcess.BeginOutputReadLine();
                updateProcess.BeginErrorReadLine();

                consoleService.UpdateProgressStatus("Installing update...", percentage: InstallingPackagesPercentage);

                // Auf den Abschluss warten mit Timeout
                updateSucceeded = await WaitForUpdateCompletionAsync(exitEvent, cancellationTokenSource);

                // Ausgabetasks beenden lassen
                await Task.WhenAll(outputTask, errorTask);

                if (updateSucceeded)
                {
                    await FinalizeSuccessfulUpdateAsync();
                    break;
                }
                else if (attempt == maxRetries)
                {
                    HandleFailedUpdate(errorTask.Result?.ToString() ?? "Update failed after multiple attempts. Access to required files may be denied.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            consoleService.CompleteProgress(false);
            consoleService.Error("Update process was cancelled or timed out.");
            WriteLongPause();
        }
        catch (Exception ex)
        {
            consoleService.CompleteProgress(false);
            consoleService.Error($"Update process failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                consoleService.Error($"Details: {ex.InnerException.Message}");
            }
            WriteLongPause();
        }
        finally
        {
            // Aufräumen
            cancellationTokenSource.Dispose();
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Prüft, ob eine Update-Prüfung durchgeführt werden soll
    /// </summary>
    private bool ShouldRunUpdate(bool force)
    {
        if (force)
            return true;

        if (!globalConfigFile.Config.AutoUpdate.Enabled)
            return false;

        var now = DateTime.Now.Ticks;
        var nextCheckTicks = globalConfigFile.Config.AutoUpdate.NextCheckTicks;
        return now > nextCheckTicks;
    }

    /// <summary>
    /// Prüft, ob ein Update angeboten werden soll
    /// </summary>
    private bool ShouldOfferUpdate(Version latestVersion)
    {
        var skipThisUpdate = latestVersion.ToVersionString() == globalConfigFile.Config.AutoUpdate.SkipVersion;
        return !skipThisUpdate && latestVersion.IsGreaterThan(spocrService.Version);
    }

    /// <summary>
    /// Bietet dem Benutzer Update-Optionen an
    /// </summary>
    private async Task OfferUpdateOptionsAsync(Version latestVersion)
    {
        consoleService.PrintImportantTitle($"A new SpocR version {latestVersion} is available");
        consoleService.Info($"Current version: {spocrService.Version}");
        consoleService.Info($"Latest version: {latestVersion}");
        consoleService.Info("");

        var options = new List<string> { "Update", "Skip this version", "Remind me later" };
        var answer = consoleService.GetSelection("Please choose an option:", options);

        switch (answer.Value)
        {
            case "Update":
                await InstallUpdateAsync();
                break;
            case "Skip this version":
                WriteSkipThisVersion();
                break;
            case "Remind me later":
            default:
                WriteLongPause();
                break;
        }
    }

    /// <summary>
    /// Erstellt ein temporäres Verzeichnis für den Update-Prozess
    /// </summary>
    private static string CreateTempUpdateDirectory()
    {
        string tempUpdateDir = Path.Combine(Path.GetTempPath(), $"SpocR_Update_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempUpdateDir);
        return tempUpdateDir;
    }

    /// <summary>
    /// Bereitet den Update-Prozess vor
    /// </summary>
    private static Process PrepareUpdateProcess(string workingDirectory)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "tool update spocr -g",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                WorkingDirectory = workingDirectory
            },
            EnableRaisingEvents = true
        };
    }

    /// <summary>
    /// Richtet die Behandlung der Prozessausgabe ein
    /// </summary>
    private (Task<StringBuilder> OutputTask, Task<StringBuilder> ErrorTask) SetupProcessOutputHandling(Process process, CancellationToken cancellationToken)
    {
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var outputCompletionSource = new TaskCompletionSource<StringBuilder>();
        var errorCompletionSource = new TaskCompletionSource<StringBuilder>();

        process.OutputDataReceived += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data))
            {
                outputCompletionSource.TrySetResult(outputBuilder);
                return;
            }

            outputBuilder.AppendLine(args.Data);
            ParseProgressAndUpdate(args.Data);
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data))
            {
                errorCompletionSource.TrySetResult(errorBuilder);
                return;
            }

            errorBuilder.AppendLine(args.Data);
            consoleService.Error(args.Data);
        };

        // Abbruch-Token mit CompletionSource verbinden
        cancellationToken.Register(() =>
        {
            outputCompletionSource.TrySetCanceled();
            errorCompletionSource.TrySetCanceled();
        });

        return (outputCompletionSource.Task, errorCompletionSource.Task);
    }

    /// <summary>
    /// Wartet auf den Abschluss des Update-Prozesses mit Timeout
    /// </summary>
    private async Task<bool> WaitForUpdateCompletionAsync(TaskCompletionSource<bool> exitEvent, CancellationTokenSource cancellationSource)
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token, cancellationSource.Token);

        try
        {
            return await exitEvent.Task.WaitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            if (timeoutSource.IsCancellationRequested)
            {
                consoleService.Error("Update process timed out after 2 minutes.");
                return false;
            }
            throw;
        }
    }

    /// <summary>
    /// Schließt ein erfolgreiches Update ab
    /// </summary>
    private async Task FinalizeSuccessfulUpdateAsync()
    {
        consoleService.UpdateProgressStatus("Update completed successfully", percentage: CompletedPercentage);
        consoleService.CompleteProgress(true);

        consoleService.Info("SpocR has been updated successfully.");
        consoleService.Info("Please restart the application to use the new version.");

        // Kurze Pause, damit der Benutzer die Meldung lesen kann
        await Task.Delay(3000);

        // Beenden der Anwendung
        Environment.Exit(0);
    }

    /// <summary>
    /// Behandelt ein fehlgeschlagenes Update
    /// </summary>
    private void HandleFailedUpdate(string errorOutput)
    {
        consoleService.CompleteProgress(false);
        consoleService.Error($"Update failed. Error: {errorOutput}");
        WriteLongPause();
    }

    /// <summary>
    /// Analysiert die Ausgabe des Update-Prozesses und aktualisiert den Fortschritt
    /// </summary>
    private void ParseProgressAndUpdate(string outputLine)
    {
        try
        {
            // Muster in der Ausgabe erkennen und Fortschritt entsprechend aktualisieren
            if (outputLine.Contains("Restoring packages"))
            {
                consoleService.UpdateProgressStatus("Restoring packages...", percentage: RestoringPackagesPercentage);
            }
            else if (outputLine.Contains("Installing"))
            {
                consoleService.UpdateProgressStatus("Installing packages...", percentage: PackagesInstalledPercentage);
            }
            else if (outputLine.Contains("Successfully"))
            {
                consoleService.UpdateProgressStatus("Finalizing installation...", percentage: FinalizingPercentage);
            }
        }
        catch
        {
            // Fehler beim Parsen ignorieren
        }
    }

    /// <summary>
    /// Schließt alle laufenden Instanzen von SpocR, um Zugriffsprobleme bei der Update-Installation zu vermeiden
    /// </summary>
    private async Task CloseRunningSpocrInstancesAsync()
    {
        consoleService.UpdateProgressStatus("Checking for other running instances...", percentage: 5);

        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(currentProcess.ProcessName);

            foreach (var process in processes)
            {
                try
                {
                    if (process.Id != currentProcess.Id)
                    {
                        consoleService.Info($"Closing running instance of SpocR (PID: {process.Id})...");
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                }
                catch (Exception ex)
                {
                    consoleService.Warn($"Could not terminate process {process.Id}: {ex.Message}");
                }
            }

            // Als zusätzliche Maßnahme, nach dotnet-Prozessen suchen, die SpocR enthalten könnten
            var dotnetProcesses = Process.GetProcessesByName("dotnet");
            foreach (var process in dotnetProcesses)
            {
                try
                {
                    if (process.Id != currentProcess.Id)
                    {
                        // Versuchen zu prüfen, ob der Prozess mit SpocR zusammenhängt
                        string processName = AutoUpdaterService.GetProcessCommandLine(process);
                        if (processName != null && processName.Contains("spocr", StringComparison.OrdinalIgnoreCase))
                        {
                            consoleService.Info($"Closing dotnet process related to SpocR (PID: {process.Id})...");
                            process.Kill();
                            await process.WaitForExitAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    consoleService.Warn($"Could not check/terminate dotnet process {process.Id}: {ex.Message}");
                }
            }

            // Kurze Pause, um sicherzustellen, dass alle Prozesse beendet sind
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            consoleService.Warn($"Error while checking for running processes: {ex.Message}");
        }
    }

    /// <summary>
    /// Versucht, die Kommandozeile eines Prozesses zu ermitteln
    /// </summary>
    private static string GetProcessCommandLine(Process process)
    {
        // if platform is not Windows, return null
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return null;

        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");

            using var objects = searcher.Get();
            foreach (var obj in objects)
            {
                return obj["CommandLine"]?.ToString();
            }
#pragma warning restore CA1416 // Validate platform compatibility
        }
        catch
        {
            // Ignorieren, falls WMI nicht funktioniert
        }

        return null;
    }

    #endregion

    #region Configuration Management

    private void WriteShortPause(bool save = true) =>
        WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, false, save);

    private void WriteLongPause(bool save = true) =>
        WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.LongPauseInMinutes, false, save);

    private void WriteSkipThisVersion(bool save = true) =>
        WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, true, save);

    private void WriteDefaults(bool save = true) =>
        WriteToGlobalConfig(globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, false, save);

    private void WriteToGlobalConfig(int pause, bool skip = false, bool save = true)
    {
        if (skip)
        {
            globalConfigFile.Config.AutoUpdate.SkipVersion = spocrService.Version.ToVersionString();
            consoleService.Info($"Version {spocrService.Version} will be skipped for updates.");
        }
        else
        {
            globalConfigFile.Config.AutoUpdate.SkipVersion = null;
        }

        var now = DateTime.Now.Ticks;
        var pauseTicks = TimeSpan.FromMinutes(pause).Ticks;
        globalConfigFile.Config.AutoUpdate.NextCheckTicks = now + pauseTicks;

        if (save) SaveGlobalConfig();
    }

    private void SaveGlobalConfig()
    {
        globalConfigFile.Save(globalConfigFile.Config);
    }

    #endregion
}

/// <summary>
/// Ausnahme, die signalisiert, dass ein Vorgang erfolgreich abgeschlossen wurde und die Anwendung neu starten sollte
/// </summary>
public class OperationCompletedException(
    string message
) : Exception(message)
{
}

public interface IPackageManager
{
    Task<Version> GetLatestVersionAsync();
}

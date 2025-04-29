using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// <remarks>
/// Erstellt eine neue Instanz des AutoUpdaterService
/// </remarks>
public class AutoUpdaterService(
    SpocrService spocrService,
    IPackageManager packageManager,
    FileManager<GlobalConfigurationModel> globalConfigFile,
    IConsoleService consoleService)
{
    #region Fields

    private readonly SpocrService _spocrService = spocrService ?? throw new ArgumentNullException(nameof(spocrService));
    private readonly IPackageManager _packageManager = packageManager ?? throw new ArgumentNullException(nameof(packageManager));
    private readonly FileManager<GlobalConfigurationModel> _globalConfigFile = globalConfigFile ?? throw new ArgumentNullException(nameof(globalConfigFile));
    private readonly IConsoleService _consoleService = consoleService ?? throw new ArgumentNullException(nameof(consoleService));

    #endregion
    #region Constructor

    #endregion

    #region Public Methods

    /// <summary>
    /// Ruft die neueste verfügbare Version von SpocR ab
    /// </summary>
    public Task<Version> GetLatestVersionAsync() => _packageManager.GetLatestVersionAsync();

    /// <summary>
    /// Führt die Überprüfung auf Updates durch und bietet dem Benutzer entsprechende Optionen an
    /// </summary>
    /// <param name="force">Update-Prüfung erzwingen, unabhängig von den Konfigurationseinstellungen</param>
    /// <param name="silent">Keine Benachrichtigungen anzeigen, außer wenn ein Update verfügbar ist</param>
    public async Task RunAsync(bool force = false, bool silent = false)
    {
        if (!ShouldRunUpdate(force))
            return;

        var latestVersion = await _packageManager.GetLatestVersionAsync();
        if (latestVersion == null)
        {
            if (!silent)
            {
                _consoleService.Info("Could not check for updates. Will try again later.");
            }
            await WriteShortPauseAsync();
            return;
        }

        if (ShouldOfferUpdate(latestVersion))
        {
            await OfferUpdateOptionsAsync(latestVersion);
            return;
        }

        await WriteShortPauseAsync();
    }

    /// <summary>
    /// Führt die Installation des Updates durch
    /// </summary>
    public void InstallUpdate()
    {
        _consoleService.Info("Updating SpocR. Please wait...");
        _consoleService.Info("The application will exit after launching the update process.");
        _consoleService.Info("Please restart SpocR after the update completes.");

        try
        {
            // Create and start a detached update process
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "tool update spocr -g",
                    UseShellExecute = true, // True to use the operating system shell
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                }
            };

            // Start the update process
            process.Start();

            // Short delay to ensure the process starts properly
            Thread.Sleep(500);

            // Exit the current process to release any file locks
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _consoleService.Error($"Failed to start update process: {ex.Message}");
            WriteLongPauseAsync();
        }
    }

    #endregion

    #region Private Update Methods

    /// <summary>
    /// Prüft, ob eine Update-Prüfung durchgeführt werden soll
    /// </summary>
    private bool ShouldRunUpdate(bool force)
    {
        if (force)
            return true;

        if (!_globalConfigFile.Config.AutoUpdate.Enabled)
            return false;

        var now = DateTime.Now.Ticks;
        var nextCheckTicks = _globalConfigFile.Config.AutoUpdate.NextCheckTicks;
        return now > nextCheckTicks;
    }

    /// <summary>
    /// Prüft, ob ein Update angeboten werden soll
    /// </summary>
    private bool ShouldOfferUpdate(Version latestVersion)
    {
        var skipThisUpdate = latestVersion.ToVersionString() == _globalConfigFile.Config.AutoUpdate.SkipVersion;
        return !skipThisUpdate && latestVersion.IsGreaterThan(_spocrService.Version);
    }

    /// <summary>
    /// Bietet dem Benutzer Update-Optionen an
    /// </summary>
    private Task OfferUpdateOptionsAsync(Version latestVersion)
    {
        _consoleService.PrintImportantTitle($"A new SpocR version {latestVersion} is available");
        _consoleService.Info($"Current version: {_spocrService.Version}");
        _consoleService.Info($"Latest version: {latestVersion}");
        _consoleService.Info("");

        var options = new List<string> { "Update", "Skip this version", "Remind me later" };
        var answer = _consoleService.GetSelection("Please choose an option:", options);

        switch (answer.Value)
        {
            case "Update":
                InstallUpdate();
                break;
            case "Skip this version":
                WriteSkipThisVersionAsync();
                break;
            case "Remind me later":
            default:
                WriteLongPauseAsync();
                break;
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Configuration Management

    /// <summary>
    /// Setzt eine kurze Wartezeit bis zur nächsten Update-Prüfung
    /// </summary>
    private Task WriteShortPauseAsync(bool save = true) =>
        WriteToGlobalConfigAsync(_globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, false, save);

    /// <summary>
    /// Setzt eine lange Wartezeit bis zur nächsten Update-Prüfung
    /// </summary>
    private Task WriteLongPauseAsync(bool save = true) =>
        WriteToGlobalConfigAsync(_globalConfigFile.Config.AutoUpdate.LongPauseInMinutes, false, save);

    /// <summary>
    /// Markiert die aktuelle Version zum Überspringen
    /// </summary>
    private Task WriteSkipThisVersionAsync(bool save = true) =>
        WriteToGlobalConfigAsync(_globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, true, save);

    /// <summary>
    /// Schreibt die Update-Konfiguration in die globale Konfiguration
    /// </summary>
    private async Task WriteToGlobalConfigAsync(int pause, bool skip = false, bool save = true)
    {
        if (skip)
        {
            _globalConfigFile.Config.AutoUpdate.SkipVersion = _spocrService.Version.ToVersionString();
            _consoleService.Info($"Version {_spocrService.Version} will be skipped for updates.");
        }
        else
        {
            _globalConfigFile.Config.AutoUpdate.SkipVersion = null;
        }

        var now = DateTime.Now.Ticks;
        var pauseTicks = TimeSpan.FromMinutes(pause).Ticks;
        _globalConfigFile.Config.AutoUpdate.NextCheckTicks = now + pauseTicks;

        if (save) await SaveGlobalConfigAsync();
    }

    /// <summary>
    /// Speichert die globale Konfiguration
    /// </summary>
    private Task SaveGlobalConfigAsync()
    {
        return _globalConfigFile.SaveAsync(_globalConfigFile.Config);
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

/// <summary>
/// Interface für Package-Manager-Dienste
/// </summary>
public interface IPackageManager
{
    /// <summary>
    /// Ruft die neueste verfügbare Version von SpocR ab
    /// </summary>
    Task<Version> GetLatestVersionAsync();
}

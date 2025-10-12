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
/// Service responsible for automatically updating the SpocR application
/// </summary>
/// <remarks>
/// Creates a new instance of the AutoUpdaterService
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
    /// Retrieves the latest available SpocR version
    /// </summary>
    public Task<Version> GetLatestVersionAsync() => _packageManager.GetLatestVersionAsync();

    /// <summary>
    /// Performs the update check and offers the user appropriate options
    /// </summary>
    /// <param name="force">Force the update check regardless of configuration settings</param>
    /// <param name="silent">Suppress notifications unless an update is available</param>
    public async Task RunAsync(bool force = false, bool silent = false)
    {
        // Early skip via environment variable (SPOCR_SKIP_UPDATE / SPOCR_NO_UPDATE)
        if (ShouldSkipByEnvironment())
        {
            if (!silent)
            {
                _consoleService.Verbose("Auto-update skipped via environment variable (SPOCR_SKIP_UPDATE / SPOCR_NO_UPDATE)");
            }
            return;
        }
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
    /// Executes the update installation
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
    /// Determines whether an update check should be performed
    /// </summary>
    private bool ShouldRunUpdate(bool force)
    {
        if (force)
            return true;

        // Also respect env skip if reached indirectly
        if (ShouldSkipByEnvironment())
            return false;

        if (!_globalConfigFile.Config.AutoUpdate.Enabled)
            return false;

        var now = DateTime.Now.Ticks;
        var nextCheckTicks = _globalConfigFile.Config.AutoUpdate.NextCheckTicks;
        return now > nextCheckTicks;
    }

    private static readonly string[] SkipEnvKeys = ["SPOCR_SKIP_UPDATE", "SPOCR_NO_UPDATE"];
    private bool ShouldSkipByEnvironment()
    {
        foreach (var key in SkipEnvKeys)
        {
            var val = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(val)) continue;
            val = val.Trim().ToLowerInvariant();
            if (val is "1" or "true" or "yes" or "on") return true;
        }
        return false;
    }

    private bool AllowDirectMajor()
    {
        var val = Environment.GetEnvironmentVariable("SPOCR_ALLOW_DIRECT_MAJOR");
        if (string.IsNullOrWhiteSpace(val)) return false;
        val = val.Trim().ToLowerInvariant();
        return val is "1" or "true" or "yes" or "on";
    }

    /// <summary>
    /// Determines whether an update should be offered
    /// </summary>
    // Made protected internal virtual for testability (BridgePolicyTests)
    protected internal virtual bool ShouldOfferUpdate(Version latestVersion)
    {
        var skipThisUpdate = latestVersion.ToVersionString() == _globalConfigFile.Config.AutoUpdate.SkipVersion;
        if (skipThisUpdate) return false;
        if (!latestVersion.IsGreaterThan(_spocrService.Version)) return false;

        // Bridge policy: require latest minor of current major before offering next major unless override
        if (!AllowDirectMajor() && latestVersion.Major > _spocrService.Version.Major)
        {
            _consoleService.Info($"Major upgrade {latestVersion.Major}.x detected. Please first update to the latest {_spocrService.Version.Major}.x (bridge release) before moving to {latestVersion.Major}.x.");
            _consoleService.Info("Set SPOCR_ALLOW_DIRECT_MAJOR=1 to override this policy (not recommended).\n");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Presents update options to the user
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
    /// Sets a short pause before the next update check
    /// </summary>
    private Task WriteShortPauseAsync(bool save = true) =>
        WriteToGlobalConfigAsync(_globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, false, save);

    /// <summary>
    /// Sets a long pause before the next update check
    /// </summary>
    private Task WriteLongPauseAsync(bool save = true) =>
        WriteToGlobalConfigAsync(_globalConfigFile.Config.AutoUpdate.LongPauseInMinutes, false, save);

    /// <summary>
    /// Marks the current version to be skipped
    /// </summary>
    private Task WriteSkipThisVersionAsync(bool save = true) =>
        WriteToGlobalConfigAsync(_globalConfigFile.Config.AutoUpdate.ShortPauseInMinutes, true, save);

    /// <summary>
    /// Writes the update configuration to the global configuration
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
    /// Persists the updated configuration
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
/// Interface for package manager services
/// </summary>
public interface IPackageManager
{
    /// <summary>
    /// Retrieves the latest available SpocR version
    /// </summary>
    Task<Version> GetLatestVersionAsync();
}

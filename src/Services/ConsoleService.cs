using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Commands;
using SpocR.Enums;
using SpocR.Models;

namespace SpocR.Services;

/// <summary>
/// Unified console service interface combining reporting, user interaction, and progress tracking
/// </summary>
public interface IConsoleService
{
    void Info(string message);
    void Error(string message);
    void Warn(string message);
    void Output(string message);
    void Verbose(string message);
    void Success(string message);
    void DrawProgressBar(int percentage, int barSize = 40);

    // Color helpers
    void Green(string message);
    void Yellow(string message);
    void Red(string message);
    void Gray(string message);

    // User interaction methods
    Choice GetSelection(string prompt, List<string> options);
    Choice GetSelectionMultiline(string prompt, List<string> options);
    bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null);
    string GetString(string prompt, string defaultValue = "", ConsoleColor? promptColor = null);

    // Formatted output methods
    void PrintTitle(string title);
    void PrintImportantTitle(string title);
    void PrintSubTitle(string title);
    void PrintSummary(IEnumerable<string> summary, string headline = null);
    void PrintTotal(string total);
    void PrintDryRunMessage(string message = null);
    void PrintConfiguration(ConfigurationModel config);
    void PrintFileActionMessage(string fileName, FileActionEnum fileAction);
    void PrintCorruptConfigMessage(string message);

    // Progress Tracking
    void StartProgress(string message);
    void CompleteProgress(bool success = true, string message = null);
    void UpdateProgressStatus(string status, bool success = true, int? percentage = null);
}


/// <summary>
/// Unified console service that combines reporting, user interaction, and progress tracking
/// </summary>
public class ConsoleService(
    IConsole console,
    CommandOptions commandOptions
) : IConsoleService
{
    private readonly object _writeLock = new();
    private readonly IConsole _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly CommandOptions _commandOptions = commandOptions ?? throw new ArgumentNullException(nameof(commandOptions));
    private readonly string _lineStar = new('*', 50);
    private readonly string _lineMinus = new('-', 50);
    private readonly string _lineUnderscore = new('_', 50);

    /// <summary>
    /// Is verbose output displayed.
    /// </summary>
    public bool IsVerbose => _commandOptions?.Verbose ?? false;

    /// <summary>
    /// Is verbose output and regular output hidden.
    /// </summary>
    public bool IsQuiet => _commandOptions?.Quiet ?? false;

    #region Basic Reporting Methods

    protected virtual void WriteLine(TextWriter writer, string message, ConsoleColor? foregroundColor, ConsoleColor? backgroundColor = default)
    {
        lock (_writeLock)
        {
            if (foregroundColor.HasValue)
            {
                _console.ForegroundColor = foregroundColor.Value;
            }

            if (backgroundColor.HasValue)
            {
                _console.BackgroundColor = backgroundColor.Value;
            }

            writer.WriteLine(message);

            if (foregroundColor.HasValue || backgroundColor.HasValue)
            {
                _console.ResetColor();
            }
        }
    }

    protected virtual void Write(TextWriter writer, string text, ConsoleColor? foregroundColor, ConsoleColor? backgroundColor = default)
    {
        lock (_writeLock)
        {
            if (foregroundColor.HasValue)
            {
                _console.ForegroundColor = foregroundColor.Value;
            }

            if (backgroundColor.HasValue)
            {
                _console.BackgroundColor = backgroundColor.Value;
            }

            writer.Write(text);

            if (foregroundColor.HasValue || backgroundColor.HasValue)
            {
                _console.ResetColor();
            }
        }
    }

    public void DrawProgressBar(int percentage, int barSize = 40)
    {
        if (IsQuiet)
        {
            return;
        }

        lock (_writeLock)
        {
            // Clear the current line instead of using SetCursorPosition
            _console.Out.Write("\r");

            percentage = Math.Max(0, Math.Min(100, percentage));
            int filledPositions = (int)Math.Floor(percentage / 100.0 * barSize);

            try
            {
                // Render the progress bar efficiently by minimizing color changes
                _console.ForegroundColor = ConsoleColor.DarkGray;
                _console.Out.Write("[");

                _console.ForegroundColor = ConsoleColor.Green;
                _console.Out.Write(new string('#', filledPositions));

                _console.ForegroundColor = ConsoleColor.DarkGray;
                _console.Out.Write(new string('-', barSize - filledPositions));
                _console.Out.Write("] ");

                _console.ForegroundColor = ConsoleColor.Cyan;
                _console.Out.Write($"{percentage}%");

                // Add spaces to ensure we overwrite any previous output of different length
                _console.ResetColor();
                _console.Out.Write("    ");
            }
            catch (Exception)
            {
                // Fail gracefully if console operations aren't supported
                _console.ResetColor();
            }
        }
    }

    public void Error(string message)
        => WriteLine(_console.Error, message, ConsoleColor.Red);

    public void Warn(string message)
        => WriteLine(_console.Out, message, ConsoleColor.Yellow);

    public void Success(string message)
        => WriteLine(_console.Out, message, ConsoleColor.Green);

    public void Output(string message)
    {
        if (IsQuiet)
        {
            return;
        }

        WriteLine(_console.Out, message, foregroundColor: null);
    }

    public void Verbose(string message)
    {
        if (!IsVerbose && !_commandOptions.Verbose)
        {
            return;
        }

        WriteLine(_console.Out, message, ConsoleColor.DarkGray);
    }

    // Color helpers
    public void Green(string message) => Success(message);
    public void Yellow(string message) => Warn(message);
    public void Red(string message) => Error(message);
    public void Gray(string message) => Verbose(message);

    public void Info(string message) => Output(message);

    #endregion

    #region User Interaction Methods

    private static int NavigateOptions(int currentIndex, int optionsCount, ConsoleKey key)
    {
        if (key == ConsoleKey.UpArrow)
        {
            return (currentIndex > 0)
                ? currentIndex - 1
                : optionsCount - 1;
        }
        else if (key == ConsoleKey.DownArrow || key == ConsoleKey.Tab)
        {
            return (currentIndex < optionsCount - 1)
                ? currentIndex + 1
                : 0;
        }

        return currentIndex;
    }

    public Choice GetSelection(string prompt, List<string> options)
    {
        if (options == null || options.Count == 0)
            throw new ArgumentException("Options list cannot be empty", nameof(options));

        var currentSelectedIndex = 0;
        var answerHint = options[currentSelectedIndex];
        var result = options[currentSelectedIndex];

        Write(_console.Out, $"{prompt} ", null);
        Write(_console.Out, $"[{string.Join(", ", options)}] ", ConsoleColor.White);
        Write(_console.Out, "(Use <tab> or <up/down> to choose)", null);
        Write(_console.Out, ": ", null);
        Write(_console.Out, answerHint, ConsoleColor.Green);

        ConsoleKeyInfo keyInfo = Console.ReadKey(true);

        // user hitted enter
        while (keyInfo.Key != ConsoleKey.Enter)
        {
            // user hitted up, down, or tab
            if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.DownArrow || keyInfo.Key == ConsoleKey.Tab)
            {
                // display next option
                ClearInput(result.Length);

                // Update selection index using shared navigation method
                currentSelectedIndex = NavigateOptions(currentSelectedIndex, options.Count, keyInfo.Key);

                // write next option to screen
                result = options[currentSelectedIndex];
                Write(_console.Out, result, ConsoleColor.Green);
            }

            keyInfo = Console.ReadKey(true);
        }

        return new Choice(currentSelectedIndex, result);
    }

    public Choice GetSelectionMultiline(string prompt, List<string> options)
    {
        if (options == null || options.Count == 0)
            throw new ArgumentException("Options list cannot be empty", nameof(options));

        var currentSelectedIndex = 0;
        var result = options[currentSelectedIndex];

        Write(_console.Out, $"{prompt} ", null);
        Write(_console.Out, "(Use <tab> or <up/down> to choose)", null);
        Write(_console.Out, ": \n\r", null);
        WriteOptions(options, currentSelectedIndex);

        ConsoleKeyInfo keyInfo = Console.ReadKey(true);

        // user hitted enter
        while (keyInfo.Key != ConsoleKey.Enter)
        {
            // user hitted up, down, or tab
            if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.DownArrow || keyInfo.Key == ConsoleKey.Tab)
            {
                try
                {
                    // Clear previous options
                    foreach (var option in options)
                    {
                        Console.SetCursorPosition(0, Console.CursorTop - 1);
                        ClearCurrentConsoleLine();
                    }

                    // Update selection index using shared navigation method
                    currentSelectedIndex = ConsoleService.NavigateOptions(currentSelectedIndex, options.Count, keyInfo.Key);

                    // write next option to screen
                    result = options[currentSelectedIndex];
                    WriteOptions(options, currentSelectedIndex);
                }
                catch (Exception)
                {
                    Output("\nBitte wählen Sie eine Option:");
                    WriteOptions(options, currentSelectedIndex);
                }
            }

            keyInfo = Console.ReadKey(true);
        }

        return new Choice(currentSelectedIndex, result);
    }

    public bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null)
    {
        var confirmed = isDefaultConfirmed;
        var output = isDefaultConfirmed ? "yes" : "no";

        Write(_console.Out, $"{prompt} ", promptColor, promptBgColor);
        Write(_console.Out, "(Use <tab> or <up/down> to choose)", null);
        Write(_console.Out, ": ", null);
        Write(_console.Out, output, ConsoleColor.Green);

        ConsoleKeyInfo keyInfo;

        keyInfo = Console.ReadKey(true);

        // user hitted enter
        while (keyInfo.Key != ConsoleKey.Enter)
        {
            // user hitted up, down, or tab
            if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.DownArrow || keyInfo.Key == ConsoleKey.Tab)
            {
                // display next option
                ClearInput(output.Length);

                // write next option to screen
                confirmed = !confirmed;
                var newOption = confirmed ? "yes" : "no";
                Write(_console.Out, newOption, ConsoleColor.Green);

                output = newOption;
            }

            keyInfo = Console.ReadKey(true);
        }

        WriteLine(_console.Out, "", null);
        WriteLine(_console.Out, "", null);

        return confirmed;
    }

    public string GetString(string prompt, string defaultValue = "", ConsoleColor? promptColor = null)
    {
        Write(_console.Out, $"{prompt} ", promptColor);

        if (!string.IsNullOrEmpty(defaultValue))
        {
            Write(_console.Out, $"[{defaultValue}] ", ConsoleColor.White);
        }

        Write(_console.Out, ": ", null);

        string result = Console.ReadLine();

        // Use default value if input is empty
        if (string.IsNullOrEmpty(result) && !string.IsNullOrEmpty(defaultValue))
        {
            result = defaultValue;
        }

        return result;
    }

    private static void ClearCurrentConsoleLine()
    {
        try
        {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }
        catch (Exception)
        {
            Console.WriteLine();
        }
    }

    private void WriteOptions(List<string> options, int currentSelectedIndex)
    {
        try
        {
            for (var i = 0; i < options.Count; i++)
            {
                if (i == currentSelectedIndex)
                {
                    var output = $"> {options[i]}{Environment.NewLine}";
                    Write(_console.Out, output, ConsoleColor.Green);
                }
                else
                {
                    var output = $"  {options[i]}{Environment.NewLine}";
                    Write(_console.Out, output, null);
                }
            }
        }
        catch (Exception ex)
        {
            Error($"Konsolenanzeige konnte nicht vollständig dargestellt werden: {ex.Message}");
            foreach (var option in options)
            {
                Output($"- {option}");
            }
        }
    }

    private static void ClearInput(int lengthToClear)
    {
        for (int i = 0; i < lengthToClear; i++)
        {
            Console.Write("\b" + " " + "\b");
        }
    }

    #endregion

    #region Formatted Output Methods

    public void PrintTitle(string title)
    {
        Output("");
        Output(_lineStar);
        Output(title);
        Output(_lineStar);
    }

    public void PrintImportantTitle(string title)
    {
        Red("");
        Red(_lineStar);
        Red(title);
        Red(_lineStar);
    }

    public void PrintSubTitle(string title)
    {
        Output("");
        Output(title);
        Output(_lineUnderscore);
    }

    public void PrintSummary(IEnumerable<string> summary, string headline = null)
    {
        using (new CursorState())
        {
            Green("");
            Green(_lineStar);
            if (headline != null)
            {
                var linePartLength = (_lineStar.Length - (headline.Length + 2)) / 2;
                var linePartPlus = new string('+', linePartLength);
                Green($"{linePartPlus} {headline} {linePartPlus}");
                Green(_lineStar);
            }

            foreach (var message in summary)
            {
                Green(message);
            }
        }
    }

    public void PrintTotal(string total)
    {
        Green(_lineMinus);
        Green(total);
        Green("");
    }

    public void PrintDryRunMessage(string message = null)
    {
        if (message != null)
            Output(message);

        Output(_lineMinus);
        Output("Run with \"dry run\" means no changes were made");
        Output(_lineMinus);
    }

    public void PrintConfiguration(ConfigurationModel config)
    {
        var jsonSettings = new JsonSerializerOptions()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Converters = {
                new JsonStringEnumConverter()
            }
        };

        var json = JsonSerializer.Serialize(config, jsonSettings);

        Warn(json);
        Output("");
    }

    public void PrintFileActionMessage(string fileName, FileActionEnum fileAction)
    {
        switch (fileAction)
        {
            case FileActionEnum.Created:
                Green($"{fileName} (created)");
                break;

            case FileActionEnum.Modified:
                Yellow($"{fileName} (modified)");
                break;

            case FileActionEnum.UpToDate:
                Gray($"{fileName} (up to date)");
                break;
        }
    }

    public void PrintCorruptConfigMessage(string message)
    {
        Warn($"Looks like your spocr.json config file is corrupt: {message}");
    }

    public void StartProgress(string message)
    {
        Green("");
        Green($"► {message}");
        Output(_lineMinus);
    }

    public void CompleteProgress(bool success = true, string message = null)
    {
        Output(_lineMinus);
        if (success)
        {
            Green($"✓ Completed");
        }
        else
        {
            Red($"✗ Failed");
        }
        if (!string.IsNullOrEmpty(message))
        {
            Gray($"  {message}");
        }
        Green("");
    }

    public void UpdateProgressStatus(string status, bool success = true, int? percentage = null)
    {
        if (success)
        {
            Gray($"  {status}");
        }
        else
        {
            Red($"  {status}");
        }

        if (percentage.HasValue)
        {
            DrawProgressBar(percentage.Value);
        }
    }

    #endregion

    #region Cursor Handling

    private class CursorState : IDisposable
    {
        private readonly bool _original;

        public CursorState()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _original = Console.CursorVisible;
                }
                else
                {
                    _original = true;
                }
            }
            catch
            {
                // some platforms throw System.PlatformNotSupportedException
                // Assume the cursor should be shown
                _original = true;
            }

            TrySetVisible(true);
        }

        private static void TrySetVisible(bool visible)
        {
            try
            {
                if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    Console.CursorVisible = visible;
                }
            }
            catch (Exception)
            {
                // Setting cursor may fail if output is piped or permission is denied
            }
        }

        public void Dispose()
        {
            TrySetVisible(_original);
        }
    }

    #endregion
}

/// <summary>
/// A response chosen by the user
/// </summary>
public class Choice(int key, string value)
{
    public int Key { get; set; } = key;
    public string Value { get; set; } = value;
}

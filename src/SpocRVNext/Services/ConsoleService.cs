using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.SpocRVNext.Cli;
using SpocR.SpocRVNext.Core;
using SpocRVNext.Configuration;
using SpocR.SpocRVNext.Models;

namespace SpocR.SpocRVNext.Services;

/// <summary>
/// Interface describing the console abstraction used by the CLI and generators.
/// </summary>
public interface IConsoleService
{
    bool IsVerbose { get; }
    bool IsQuiet { get; }

    void Info(string message);
    void Error(string message);
    void Warn(string message);
    void Output(string message);
    void Verbose(string message);
    void Success(string message);
    void DrawProgressBar(int percentage, int barSize = 40);

    void Green(string message);
    void Yellow(string message);
    void Red(string message);
    void Gray(string message);

    Choice GetSelection(string prompt, List<string> options);
    Choice GetSelectionMultiline(string prompt, List<string> options);
    bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null);
    string GetString(string prompt, string defaultValue = "", ConsoleColor? promptColor = null);

    void PrintTitle(string title);
    void PrintImportantTitle(string title);
    void PrintSubTitle(string title);
    void PrintSummary(IEnumerable<string> summary, string? headline = null);
    void PrintTotal(string total);
    void PrintDryRunMessage(string? message = null);
    void PrintConfiguration(ConfigurationModel config);
    void PrintFileActionMessage(string fileName, FileActionEnum fileAction);
    void PrintCorruptConfigMessage(string message);

    void StartProgress(string message);
    void CompleteProgress(bool success = true, string? message = null);
    void UpdateProgressStatus(string status, bool success = true, int? percentage = null);
}

/// <summary>
/// System.Console based implementation intentionally independent from external console libraries.
/// </summary>
public sealed class ConsoleService : IConsoleService
{
    private readonly CommandOptions _commandOptions;
    private readonly object _writeLock = new();

    private readonly string _lineStar = new('*', 50);
    private readonly string _lineMinus = new('-', 50);
    private readonly string _lineUnderscore = new('_', 50);

    public ConsoleService(CommandOptions commandOptions)
    {
        _commandOptions = commandOptions ?? throw new ArgumentNullException(nameof(commandOptions));
    }

    public bool IsVerbose => _commandOptions?.Verbose ?? false;
    public bool IsQuiet => _commandOptions?.Quiet ?? false;

    private static TextWriter StdOut => Console.Out;
    private static TextWriter StdErr => Console.Error;

    public void Info(string message) => Output(message);

    public void Error(string message) => WriteLine(StdErr, message, ConsoleColor.Red);

    public void Warn(string message) => WriteLine(StdOut, message, ConsoleColor.Yellow);

    public void Output(string message)
    {
        if (IsQuiet)
        {
            return;
        }

        WriteLine(StdOut, message, foregroundColor: null);
    }

    public void Verbose(string message)
    {
        if (!IsVerbose || IsQuiet)
        {
            return;
        }

        WriteLine(StdOut, message, ConsoleColor.DarkGray);
    }

    public void Success(string message) => WriteLine(StdOut, message, ConsoleColor.Green);

    public void DrawProgressBar(int percentage, int barSize = 40)
    {
        if (IsQuiet)
        {
            return;
        }

        percentage = Math.Clamp(percentage, 0, 100);
        barSize = Math.Max(10, barSize);

        lock (_writeLock)
        {
            try
            {
                Console.CursorVisible = false;
            }
            catch
            {
                // ignore cursor visibility errors (stdout redirected)
            }

            StdOut.Write('\r');

            var filled = (int)Math.Round(barSize * (percentage / 100.0));
            var empty = barSize - filled;

            TrySetColors(ConsoleColor.DarkGray, null);
            StdOut.Write('[');

            TrySetColors(ConsoleColor.Green, null);
            StdOut.Write(new string('#', filled));

            TrySetColors(ConsoleColor.DarkGray, null);
            StdOut.Write(new string('-', empty));
            StdOut.Write("] ");

            TrySetColors(ConsoleColor.Cyan, null);
            StdOut.Write($"{percentage}%");
            TryResetColors();

            if (percentage == 100)
            {
                StdOut.WriteLine();
                try
                {
                    Console.CursorVisible = true;
                }
                catch
                {
                }
            }
        }
    }

    public void Green(string message) => Success(message);
    public void Yellow(string message) => Warn(message);
    public void Red(string message) => Error(message);
    public void Gray(string message) => Verbose(message);

    public Choice GetSelection(string prompt, List<string> options)
    {
        return GetSelectionInternal(prompt, options, multiline: false);
    }

    public Choice GetSelectionMultiline(string prompt, List<string> options)
    {
        return GetSelectionInternal(prompt, options, multiline: true);
    }

    public bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null)
    {
        var defaultLabel = isDefaultConfirmed ? "Y/n" : "y/N";
        while (true)
        {
            Write(StdOut, $"{prompt} ", promptColor, promptBgColor);
            Write(StdOut, $"[{defaultLabel}]", ConsoleColor.White);
            Write(StdOut, ": ", null);

            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return isDefaultConfirmed;
            }

            line = line.Trim();
            if (line.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (line.Equals("n", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Warn("Please answer with 'y' or 'n'.");
        }
    }

    public string GetString(string prompt, string defaultValue = "", ConsoleColor? promptColor = null)
    {
        Write(StdOut, $"{prompt} ", promptColor);
        if (!string.IsNullOrEmpty(defaultValue))
        {
            Write(StdOut, $"[{defaultValue}] ", ConsoleColor.White);
        }

        Write(StdOut, ": ", null);
        var response = Console.ReadLine();
        if (string.IsNullOrEmpty(response))
        {
            response = defaultValue;
        }

        return response ?? string.Empty;
    }

    public void PrintTitle(string title)
    {
        Output(string.Empty);
        Output(_lineStar);
        Output(title);
        Output(_lineStar);
    }

    public void PrintImportantTitle(string title)
    {
        Output(string.Empty);
        WriteLine(StdOut, _lineStar, ConsoleColor.Red);
        WriteLine(StdOut, title, ConsoleColor.Red);
        WriteLine(StdOut, _lineStar, ConsoleColor.Red);
    }

    public void PrintSubTitle(string title)
    {
        Output(string.Empty);
        Output(title);
        Output(_lineUnderscore);
    }

    public void PrintSummary(IEnumerable<string> summary, string? headline = null)
    {
        Output(string.Empty);
        Success(_lineStar);

        if (!string.IsNullOrWhiteSpace(headline))
        {
            var normalized = headline.Trim();
            var padding = (_lineStar.Length - (normalized.Length + 2)) / 2;
            var pad = new string('+', Math.Max(1, padding));
            Success($"{pad} {normalized} {pad}");
            Success(_lineStar);
        }

        foreach (var message in summary)
        {
            Success(message);
        }
    }

    public void PrintTotal(string total)
    {
        Success(_lineMinus);
        Success(total);
        Success(string.Empty);
    }

    public void PrintDryRunMessage(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Output(message);
        }

        Output(_lineMinus);
        Output("Run with \"dry run\" means no changes were made");
        Output(_lineMinus);
    }

    public void PrintConfiguration(ConfigurationModel config)
    {
        if (config == null)
        {
            Warn("No configuration available to display.");
            return;
        }

        ConfigurationModel? clone = null;
        try
        {
            clone = new ConfigurationModel
            {
                Version = config.Version,
                TargetFramework = config.TargetFramework,
                Project = config.Project is { } project
                    ? new ProjectModel
                    {
                        DataBase = project.DataBase,
                        Output = project.Output,
                        DefaultSchemaStatus = project.DefaultSchemaStatus,
                        IgnoredSchemas = project.IgnoredSchemas,
                        IgnoredProcedures = project.IgnoredProcedures,
                        JsonTypeLogLevel = project.JsonTypeLogLevel,
                        Role = project.Role ?? new RoleModel()
                    }
                    : new ProjectModel(),
                Schema = config.Schema
            };

#pragma warning disable CS0618
            if (clone?.Project?.Role?.Kind == RoleKindEnum.Default && string.IsNullOrWhiteSpace(clone.Project.Role.LibNamespace))
            {
                clone.Project.Role = new RoleModel();
            }
#pragma warning restore CS0618
        }
        catch
        {
            clone = config;
        }

        var jsonSettings = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        var json = JsonSerializer.Serialize(clone, jsonSettings);
        Warn(json);
        Output(string.Empty);
    }

    public void PrintFileActionMessage(string fileName, FileActionEnum fileAction)
    {
        switch (fileAction)
        {
            case FileActionEnum.Created:
                Success($"{fileName} (created)");
                break;
            case FileActionEnum.Modified:
                Yellow($"{fileName} (modified)");
                break;
            case FileActionEnum.UpToDate:
                Gray($"{fileName} (up to date)");
                break;
            default:
                Output($"{fileName} ({fileAction})");
                break;
        }
    }

    public void PrintCorruptConfigMessage(string message)
    {
        Warn($"Looks like your spocr.json config file is corrupt: {message}");
    }

    public void StartProgress(string message)
    {
        Success(string.Empty);
        Success($"► {message}");
        Output(_lineMinus);
    }

    public void CompleteProgress(bool success = true, string? message = null)
    {
        Output(_lineMinus);
        if (success)
        {
            Success("✓ Completed");
        }
        else
        {
            Error("✗ Failed");
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            Gray($"  {message}");
        }

        Success(string.Empty);
    }

    public void UpdateProgressStatus(string status, bool success = true, int? percentage = null)
    {
        if (success)
        {
            Gray($"  {status}");
        }
        else
        {
            Error($"  {status}");
        }

        if (percentage.HasValue)
        {
            DrawProgressBar(percentage.Value);
        }
    }

    private Choice GetSelectionInternal(string prompt, List<string> options, bool multiline)
    {
        if (options == null || options.Count == 0)
        {
            throw new ArgumentException("Options list cannot be empty", nameof(options));
        }

        if (multiline)
        {
            Output(prompt);
            for (var i = 0; i < options.Count; i++)
            {
                Output($"  [{i + 1}] {options[i]}");
            }
        }
        else
        {
            Output($"{prompt} [{string.Join(", ", options)}]");
        }

        while (true)
        {
            Write(StdOut, "Select option (number): ", ConsoleColor.Green);
            var line = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(line) && int.TryParse(line, out var index))
            {
                index -= 1;
                if (index >= 0 && index < options.Count)
                {
                    return new Choice(index, options[index]);
                }
            }

            Warn("Invalid selection, please enter the number shown in the list.");
        }
    }

    private void WriteLine(TextWriter writer, string message, ConsoleColor? foregroundColor, ConsoleColor? backgroundColor = null)
    {
        lock (_writeLock)
        {
            TrySetColors(foregroundColor, backgroundColor);
            writer.WriteLine(message);
            if (foregroundColor.HasValue || backgroundColor.HasValue)
            {
                TryResetColors();
            }
        }
    }

    private void Write(TextWriter writer, string text, ConsoleColor? foregroundColor, ConsoleColor? backgroundColor = null)
    {
        lock (_writeLock)
        {
            TrySetColors(foregroundColor, backgroundColor);
            writer.Write(text);
            if (foregroundColor.HasValue || backgroundColor.HasValue)
            {
                TryResetColors();
            }
        }
    }

    private static void TrySetColors(ConsoleColor? foregroundColor, ConsoleColor? backgroundColor)
    {
        try
        {
            if (foregroundColor.HasValue)
            {
                Console.ForegroundColor = foregroundColor.Value;
            }

            if (backgroundColor.HasValue)
            {
                Console.BackgroundColor = backgroundColor.Value;
            }
        }
        catch
        {
            // ignore coloring failures
        }
    }

    private static void TryResetColors()
    {
        try
        {
            Console.ResetColor();
        }
        catch
        {
            // ignore reset failures (e.g. redirected output)
        }
    }
}

/// <summary>
/// Simple container representing an answer chosen by the user.
/// </summary>
public sealed class Choice
{
    public Choice(int key, string value)
    {
        Key = key;
        Value = value;
    }

    public int Key { get; set; }
    public string Value { get; set; }
}


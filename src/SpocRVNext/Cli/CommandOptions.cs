#nullable disable

using System;

namespace SpocR.SpocRVNext.Cli;

public interface ICommandOptions
{
    string Path { get; }
    bool Verbose { get; }
    bool Debug { get; }
    bool NoCache { get; }
    string Procedure { get; }
}

public class CommandOptions : ICommandOptions
{
    private readonly CliCommandOptions _current = new();

    public CommandOptions()
    {
    }

    public CommandOptions(ICommandOptions options)
    {
        Update(options);
    }

    public void Update(ICommandOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _current.Path = options.Path?.Trim() ?? string.Empty;
        _current.Verbose = options.Verbose;
        _current.Debug = options.Debug;
        _current.NoCache = options.NoCache;
        _current.Procedure = options.Procedure?.Trim() ?? string.Empty;
    }

    public string Path => _current.Path?.Trim();
    public bool Verbose => _current.Verbose;
    public bool Debug => _current.Debug;
    public bool NoCache => _current.NoCache;
    public string Procedure => _current.Procedure;
}

/// <summary>
/// Mutable implementation used by the CLI to pass parsed options to the runtime.
/// </summary>
public sealed class CliCommandOptions : ICommandOptions
{
    public string Path { get; set; }
    public bool Verbose { get; set; }
    public bool Debug { get; set; }
    public bool NoCache { get; set; }
    public string Procedure { get; set; }
}

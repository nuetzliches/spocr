using System;

namespace SpocR.Commands;

public interface ICommandOptions
{
    string Path { get; }
    bool DryRun { get; }
    bool Force { get; }
    bool Quiet { get; }
    bool Verbose { get; }
    bool NoVersionCheck { get; set; }
    bool NoAutoUpdate { get; set; }
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
        _current.DryRun = options.DryRun;
        _current.Force = options.Force;
        _current.Quiet = options.Quiet;
        _current.Verbose = options.Verbose;
        _current.NoVersionCheck = options.NoVersionCheck;
        _current.NoAutoUpdate = options.NoAutoUpdate;
        _current.Debug = options.Debug;
        _current.NoCache = options.NoCache;
        _current.Procedure = options.Procedure?.Trim() ?? string.Empty;
    }

    public string Path => _current.Path?.Trim();
    public bool DryRun => _current.DryRun;
    public bool Force => _current.Force;
    public bool Quiet => _current.Quiet;
    public bool Verbose => _current.Verbose;

    public bool NoVersionCheck
    {
        get => _current.NoVersionCheck;
        set => _current.NoVersionCheck = value;
    }

    public bool NoAutoUpdate
    {
        get => _current.NoAutoUpdate;
        set => _current.NoAutoUpdate = value;
    }

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
    public bool DryRun { get; set; }
    public bool Force { get; set; }
    public bool Quiet { get; set; }
    public bool Verbose { get; set; }
    public bool NoVersionCheck { get; set; }
    public bool NoAutoUpdate { get; set; }
    public bool Debug { get; set; }
    public bool NoCache { get; set; }
    public string Procedure { get; set; }
}

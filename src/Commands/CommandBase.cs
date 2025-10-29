using System;
using System.Threading;

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
    private static readonly AsyncLocal<ICommandOptions> CurrentOptions = new();
    private static readonly MutableCommandOptions DefaultOptions = new();
    private readonly ICommandOptions _options;

    public CommandOptions()
    {
    }

    public CommandOptions(ICommandOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        CurrentOptions.Value = options;
    }

    private ICommandOptions EffectiveOptions => _options ?? CurrentOptions.Value ?? DefaultOptions;

    public string Path => EffectiveOptions.Path?.Trim();
    public bool DryRun => EffectiveOptions.DryRun;
    public bool Force => EffectiveOptions.Force;
    public bool Quiet => EffectiveOptions.Quiet;
    public bool Verbose => EffectiveOptions.Verbose;

    public bool NoVersionCheck
    {
        get => EffectiveOptions.NoVersionCheck;
        set
        {
            if (_options != null)
            {
                _options.NoVersionCheck = value;
            }
            else
            {
                var target = CurrentOptions.Value ?? DefaultOptions;
                target.NoVersionCheck = value;
                CurrentOptions.Value = target;
            }
        }
    }

    public bool NoAutoUpdate
    {
        get => EffectiveOptions.NoAutoUpdate;
        set
        {
            if (_options != null)
            {
                _options.NoAutoUpdate = value;
            }
            else
            {
                var target = CurrentOptions.Value ?? DefaultOptions;
                target.NoAutoUpdate = value;
                CurrentOptions.Value = target;
            }
        }
    }

    public bool Debug => EffectiveOptions.Debug;
    public bool NoCache => (EffectiveOptions as dynamic).NoCache; // dynamic to allow older instances; guaranteed on new builds
    public string Procedure => EffectiveOptions.Procedure;

    private sealed class MutableCommandOptions : ICommandOptions
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

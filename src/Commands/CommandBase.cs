using System;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Enums;
using SpocR.Utils;

namespace SpocR.Commands;

public abstract class CommandBase : IAppCommand, ICommandOptions
{
    [Option("-p|--path", "Path to spocr.json, eg. the path to your project itself", CommandOptionType.SingleValue)]
    public virtual string Path { get; set; }

    [Option("-d|--dry-run", "Run command without making any changes", CommandOptionType.NoValue)]
    public virtual bool DryRun { get; set; }

    [Option("-f|--force", "Run command even if we got warnings", CommandOptionType.NoValue)]
    public virtual bool Force { get; set; }

    [Option("-s|--silent", "Run without user interactions and dont check for updates", CommandOptionType.NoValue)]
    public virtual bool Quiet { get; set; }

    [Option("-v|--verbose", "Show non necessary information", CommandOptionType.NoValue)]
    public virtual bool Verbose { get; set; }

    [Option("-nvc|--no-version-check", "Ignore version missmatch between installation and config file", CommandOptionType.NoValue)]
    public virtual bool NoVersionCheck { get; set; }

    [Option("-nau|--no-auto-update", "Ignore auto update", CommandOptionType.NoValue)]
    public virtual bool NoAutoUpdate { get; set; }

    [Option("--debug", "Use debug environment.", CommandOptionType.NoValue)]
    public virtual bool Debug { get; set; }

    public virtual async Task<int> OnExecuteAsync()
    {
        DirectoryUtils.SetBasePath(Path);
        return await Task.FromResult((int)ExecuteResultEnum.Succeeded);
    }

    public ICommandOptions CommandOptions => new CommandOptions(this);
}

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
    }
}

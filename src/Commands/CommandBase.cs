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
        return await Task.FromResult((int)EExecuteResult.Succeeded);
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

public class CommandOptions(ICommandOptions options) : ICommandOptions
{

    // Parameterloser Konstruktor
    public CommandOptions() : this(new EmptyCommandOptions())
    {
    }

    public string Path => options?.Path?.Trim();
    public bool DryRun => options?.DryRun ?? false;
    public bool Force => options?.Force ?? false;
    public bool Quiet => options?.Quiet ?? false;
    public bool Verbose { get; set; } = false;
    public bool NoVersionCheck { get; set; } = false;
    public bool NoAutoUpdate { get; set; } = false;
    public bool Debug => options?.Debug ?? false;
}

// Hilfsklasse für den parameterlosen Konstruktor
internal class EmptyCommandOptions : ICommandOptions
{
    public string Path => null;
    public bool DryRun => false;
    public bool Force => false;
    public bool Quiet => false;
    public bool Verbose { get; set; } = false;
    public bool NoVersionCheck { get; set; } = false;
    public bool NoAutoUpdate { get; set; } = false;
    public bool Debug => false;
}

using System.IO;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Commands.Project;
using SpocR.Managers;
using SpocR.Utils;

namespace SpocR.Commands.Spocr;

[HelpOption("-?|-h|--help")]
[Command("create", Description = "(Deprecated v5) Creates a legacy spocr.json config. Use 'spocr init' for .env bootstrap.")]
public class CreateCommand(
    SpocrManager spocrManager,
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager), ICreateCommandOptions
{

    [Option("-n|--name", "Name of your Project", CommandOptionType.SingleValue)]
    public string DisplayName { get; set; }

    [Option("-tf|--targetframework", "TargetFramework", CommandOptionType.SingleValue)]
    public string TargetFramework { get; set; }

    [Option("-ns|--namespace", "Namespace of your .NET Core Project", CommandOptionType.SingleValue)]
    public string Namespace { get; set; } = new DirectoryInfo(Directory.GetCurrentDirectory()).Name;

    [Option("-r|--role", "Role", CommandOptionType.SingleValue)]
    public string Role { get; set; }

    [Option("-lns|--libNamespace", "Namespace of your .NET Core Library", CommandOptionType.SingleValue)]
    public string LibNamespace { get; set; }

    [Option("-i|--identity", "Identity", CommandOptionType.SingleValue)]
    public string Identity { get; set; }

    public ICreateCommandOptions CreateCommandOptions => new CreateCommandOptions(this);

    public override async Task<int> OnExecuteAsync()
    {
        await base.OnExecuteAsync();

        // Read Path to spocr.json from Project configuration
        if (!string.IsNullOrEmpty(Project))
        {
            var project = spocrProjectManager.FindByName(Project);
            if (project != null)
                Path = project.ConfigFile;
        }
        else if (!string.IsNullOrEmpty(Path) && !DirectoryUtils.IsPath(Path))
        {
            var project = spocrProjectManager.FindByName(Path);
            Path = project.ConfigFile;
        }

        var result = await spocrManager.CreateAsync(CreateCommandOptions);
        return CommandResultMapper.Map(result);
    }
}

public interface ICreateCommandOptions : ICommandOptions, IProjectCommandOptions
{
    string TargetFramework { get; }
    string Namespace { get; }
    string Role { get; }
    string LibNamespace { get; }
    string Identity { get; }
}

public class CreateCommandOptions(
    ICreateCommandOptions options
) : CommandOptions(options), ICreateCommandOptions
{
    public string DisplayName => options.DisplayName?.Trim();
    public string TargetFramework => options.TargetFramework?.Trim();
    public string Namespace => options.Namespace?.Trim();
    public string Role => options.Role?.Trim();
    public string LibNamespace => options.LibNamespace?.Trim();
    public string Identity => options.Identity?.Trim();
}

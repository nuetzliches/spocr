using McMaster.Extensions.CommandLineUtils;

namespace SpocR.Commands.Project;

public class ProjectCommandBase() : CommandBase, IProjectCommandOptions
{
    [Option("-n|--name", "Project name and identifier", CommandOptionType.SingleValue)]
    public string DisplayName { get; set; }

    public IProjectCommandOptions ProjectCommandOptions => new ProjectCommandOptions(this);
}

public interface IProjectCommandOptions : ICommandOptions
{
    string DisplayName { get; }
}

public class ProjectCommandOptions(
    IProjectCommandOptions options
) : CommandOptions(options), IProjectCommandOptions
{
    public string DisplayName => options.DisplayName?.Trim();
}

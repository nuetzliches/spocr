using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Project
{
    public class ProjectCommandBase : CommandBase, IProjectCommandOptions
    {
        [Option("-n|--name", "Project name and identifier", CommandOptionType.SingleValue)]
        public string DisplayName { get; set; }

        protected readonly SpocrProjectManager SpocrProjectManager;

        public IProjectCommandOptions ProjectCommandOptions => new ProjectCommandOptions(this);

        public ProjectCommandBase(SpocrProjectManager spocrProjectManager)
        {
            SpocrProjectManager = spocrProjectManager;
        }
    }

    public interface IProjectCommandOptions : ICommandOptions
    {
        string DisplayName { get; }
    }

    public class ProjectCommandOptions : CommandOptions, IProjectCommandOptions
    {
        private readonly IProjectCommandOptions _options;
        public ProjectCommandOptions(IProjectCommandOptions options)
            : base(options)
        {
            _options = options;
        }

        public string DisplayName => _options.DisplayName?.Trim();
    }
}

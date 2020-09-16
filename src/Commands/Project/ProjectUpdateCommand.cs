using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Project
{
    [HelpOption("-?|-h|--help")]
    [Command("update", Description = "Update an existing SpocR Project")]
    public class ProjectUpdateCommand : ProjectCommandBase, IProjectUpdateCommandOptions
    {

        [Option("-nn|--new-name", "New Project name and identifier", CommandOptionType.SingleValue)]
        public string NewDisplayName { get; set; }

        public ProjectUpdateCommandOptions ProjectUpdateCommandOptions => new ProjectUpdateCommandOptions(this);

        public ProjectUpdateCommand(SpocrProjectManager spocrProjectManager)
        : base(spocrProjectManager)
        { }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)SpocrProjectManager.Update(ProjectUpdateCommandOptions);
        }
    }

    public interface IProjectUpdateCommandOptions : IProjectCommandOptions
    {
        string NewDisplayName { get; }
    }

    public class ProjectUpdateCommandOptions : ProjectCommandOptions, IProjectUpdateCommandOptions
    {
        private readonly IProjectUpdateCommandOptions _options;
        public ProjectUpdateCommandOptions(IProjectUpdateCommandOptions options)
            : base(options)
        {
            _options = options;
        }

        public string NewDisplayName => _options.NewDisplayName?.Trim();
    }
}

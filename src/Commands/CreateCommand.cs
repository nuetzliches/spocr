using System.IO;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Commands.Project;
using SpocR.Managers;

namespace SpocR.Commands
{
    [HelpOption("-?|-h|--help")]
    [Command("create", Description = "Creates a new SpocR Config")]
    public class CreateCommand : CommandBase, ICreateCommandOptions
    {
        private readonly SpocrManager _spocrManager;

        [Option("-n|--name", "Name of your Project", CommandOptionType.SingleValue)]
        public string DisplayName { get; set; }

        [Option("-ns|--namespace", "Namespace of your .NET Core Project", CommandOptionType.SingleValue)]
        public string Namespace { get; set; } = new DirectoryInfo(Directory.GetCurrentDirectory()).Name;

        [Option("-r|--role", "Role", CommandOptionType.SingleValue)]
        public string Role { get; set; }

        [Option("-lns|--libNamespace", "Namespace of your .NET Core Library", CommandOptionType.SingleValue)]
        public string LibNamespace { get; set; }

        [Option("-i|--identity", "Identity", CommandOptionType.SingleValue)]
        public string Identity { get; set; }

        public ICreateCommandOptions CreateCommandOptions => new CreateCommandOptions(this);

        public CreateCommand(SpocrManager spocrManager)
        {
            _spocrManager = spocrManager;
        }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)_spocrManager.Create(CreateCommandOptions);
        }
    }

    public interface ICreateCommandOptions : ICommandOptions, IProjectCommandOptions
    {
        string Namespace { get; }
        string Role { get; }
        string LibNamespace { get; }
        string Identity { get; }
    }

    public class CreateCommandOptions : CommandOptions, ICreateCommandOptions
    {
        private readonly ICreateCommandOptions _options;
        public CreateCommandOptions(ICreateCommandOptions options)
            : base(options)
        {
            _options = options;
        }

        public string DisplayName => _options.DisplayName?.Trim();
        public string Namespace => _options.Namespace?.Trim();
        public string Role => _options.Role?.Trim();
        public string LibNamespace => _options.LibNamespace?.Trim();
        public string Identity => _options.Identity?.Trim();
    }
}

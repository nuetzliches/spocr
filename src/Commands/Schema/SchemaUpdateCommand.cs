using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Schema
{
    [HelpOption("-?|-h|--help")]
    [Command("update", Description = "Update an existing SpocR Schema")]
    public class SchemaUpdateCommand : SchemaCommandBase, ISchemaUpdateCommandOptions
    {
        [Option("--name", "Schema name", CommandOptionType.SingleValue)]
        public string SchemaName { get; set; }

        [Option("--status", "Set schema status to Build or Ignored", CommandOptionType.SingleValue)]
        public string Status { get; set; }

        public SchemaUpdateCommandOptions SchemaUpdateCommandOptions => new SchemaUpdateCommandOptions(this);

        public SchemaUpdateCommand(SpocrSchemaManager spocrSchemaManager, SpocrProjectManager spocrProjectManager)
        : base(spocrSchemaManager, spocrProjectManager)
        { }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)SpocrSchemaManager.Update(SchemaUpdateCommandOptions);
        }
    }

    public interface ISchemaUpdateCommandOptions : ICommandOptions
    {
        string SchemaName { get; }
        string Status { get; }
    }

    public class SchemaUpdateCommandOptions : CommandOptions, ISchemaUpdateCommandOptions
    {
        private readonly ISchemaUpdateCommandOptions _options;
        public SchemaUpdateCommandOptions(ISchemaUpdateCommandOptions options)
            : base(options)
        {
            _options = options;
        }

        public string SchemaName => _options.SchemaName?.Trim();
        public string Status => _options.Status?.Trim();
    }
}

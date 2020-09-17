using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Schema
{
    [HelpOption("-?|-h|--help")]
    [Command("update", Description = "Update an existing SpocR Schema")]
    public class SchemaUpdateCommand : SchemaCommandBase, ISchemaUpdateCommandOptions
    {

        [Option("--status", "Set schema status to Build or Ignored", CommandOptionType.SingleValue)]
        public string Status { get; set; }

        public SchemaUpdateCommandOptions SchemaUpdateCommandOptions => new SchemaUpdateCommandOptions(this);

        public SchemaUpdateCommand(SpocrSchemaManager spocrSchemaManager)
        : base(spocrSchemaManager)
        { }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)SpocrSchemaManager.Update(SchemaUpdateCommandOptions);
        }
    }

    public interface ISchemaUpdateCommandOptions : ICommandOptions
    {
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

        public string Status => _options.Status?.Trim();
    }
}

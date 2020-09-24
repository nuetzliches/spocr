using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;

namespace SpocR.Commands.Schema
{
    [HelpOption("-?|-h|--help")]
    [Command("ls", Description = "List all SpocR Schemas")]
    public class SchemaListCommand : SchemaCommandBase
    {
        public SchemaListCommand(SpocrSchemaManager spocrSchemaManager, SpocrProjectManager spocrProjectManager)
        : base(spocrSchemaManager, spocrProjectManager)
        { }

        public override int OnExecute()
        {
            base.OnExecute();
            return (int)SpocrSchemaManager.List(CommandOptions);
        }
    }
}

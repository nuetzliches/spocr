using SpocR.Managers;

namespace SpocR.Commands.Schema
{
    public class SchemaCommandBase : CommandBase
    {
        protected readonly SpocrSchemaManager SpocrSchemaManager;
        public SchemaCommandBase(SpocrSchemaManager spocrSchemaManager)
        {
            SpocrSchemaManager = spocrSchemaManager;
        }
    }
}

using SpocR.Commands.Spocr;
using SpocR.Managers;

namespace SpocR.Commands.Schema
{
    public class SchemaCommandBase : SpocrCommandBase
    {
        protected readonly SpocrSchemaManager SpocrSchemaManager;
        public SchemaCommandBase(SpocrSchemaManager spocrSchemaManager, SpocrProjectManager spocrProjectManager) 
        : base(spocrProjectManager)
        {
            SpocrSchemaManager = spocrSchemaManager;
        }
    }
}

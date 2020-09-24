using McMaster.Extensions.CommandLineUtils;
using SpocR.Commands.Spocr;
using SpocR.Managers;

namespace SpocR.Commands.StoredProcdure
{
    public class StoredProcdureCommandBase : SpocrCommandBase, IStoredProcedureCommandOptions
    {
        protected readonly SpocrStoredProcdureManager SpocrStoredProcdureManager;

        [Option("-sc|--schema", "Schmema name and identifier", CommandOptionType.SingleValue)]
        public string SchemaName { get; set; }

        public IStoredProcedureCommandOptions StoredProcedureCommandOptions => new StoredProcedureCommandOptions(this);

        public StoredProcdureCommandBase(SpocrStoredProcdureManager spocrStoredProcdureManager, SpocrProjectManager spocrProjectManager) 
        : base(spocrProjectManager)
        {
            SpocrStoredProcdureManager = spocrStoredProcdureManager;
        }
    }

    public interface IStoredProcedureCommandOptions : ICommandOptions
    {
        string SchemaName { get; }
    }

    public class StoredProcedureCommandOptions : CommandOptions, IStoredProcedureCommandOptions
    {
        private readonly IStoredProcedureCommandOptions _options;
        public StoredProcedureCommandOptions(IStoredProcedureCommandOptions options)
            : base(options)
        {
            _options = options;
        }

        public string SchemaName => _options.SchemaName?.Trim();
    }
}

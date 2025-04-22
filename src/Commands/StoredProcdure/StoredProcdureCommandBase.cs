using McMaster.Extensions.CommandLineUtils;
using SpocR.Commands.Spocr;
using SpocR.Managers;

namespace SpocR.Commands.StoredProcdure;

public class StoredProcdureCommandBase(
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager), IStoredProcedureCommandOptions
{
    [Option("-sc|--schema", "Schmema name and identifier", CommandOptionType.SingleValue)]
    public string SchemaName { get; set; }

    public IStoredProcedureCommandOptions StoredProcedureCommandOptions => new StoredProcedureCommandOptions(this);
}

public interface IStoredProcedureCommandOptions : ICommandOptions
{
    string SchemaName { get; }
}

public class StoredProcedureCommandOptions(
    IStoredProcedureCommandOptions options
) : CommandOptions(options), IStoredProcedureCommandOptions
{
    public string SchemaName => options.SchemaName?.Trim();
}

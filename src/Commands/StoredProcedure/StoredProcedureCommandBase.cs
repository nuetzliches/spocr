using McMaster.Extensions.CommandLineUtils;
using SpocR.Commands.Spocr;
using SpocR.Managers;

namespace SpocR.Commands.StoredProcedure;

public class StoredProcedureCommandBase(
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager), IStoredProcedureCommandOptions
{
    [Option("-sc|--schema", "Schmema name and identifier", CommandOptionType.SingleValue)]
    public string SchemaName { get; set; }

    [Option("--json", "Outputs raw JSON only (suppresses warnings unless --verbose)", CommandOptionType.NoValue)]
    public bool Json { get; set; }

    public IStoredProcedureCommandOptions StoredProcedureCommandOptions => new StoredProcedureCommandOptions(this);
}

public interface IStoredProcedureCommandOptions : ICommandOptions
{
    string SchemaName { get; }
    bool Json { get; }
}

public class StoredProcedureCommandOptions(
    IStoredProcedureCommandOptions options
) : CommandOptions(options), IStoredProcedureCommandOptions
{
    public string SchemaName => options.SchemaName?.Trim();
    public bool Json => options.Json;
}

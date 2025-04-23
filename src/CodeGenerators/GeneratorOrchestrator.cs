using SpocR.CodeGenerators.Extensions;
using SpocR.CodeGenerators.Models;

namespace SpocR.CodeGenerators;

public class GeneratorOrchestrator(
    InputGenerator inputGenerator,
    OutputGenerator outputGenerator,
    ModelGenerator modelGenerator,
    TableTypeGenerator tableTypeGenerator,
    StoredProcedureGenerator storedProcedureGenerator
)
{
    public void GenerateAll(bool isDryRun)
    {
        GenerateDataContextTableTypes(isDryRun);
        GenerateDataContextInputs(isDryRun);
        GenerateDataContextOutputs(isDryRun);
        GenerateDataContextModels(isDryRun);
        GenerateDataContextStoredProcedures(isDryRun);
    }

    public void GenerateDataContextTableTypes(bool isDryRun)
    {
        tableTypeGenerator.GenerateDataContextTableTypes(isDryRun);
    }

    public void GenerateDataContextInputs(bool isDryRun)
    {
        inputGenerator.GenerateDataContextInputs(isDryRun);
    }

    public void GenerateDataContextOutputs(bool isDryRun)
    {
        outputGenerator.GenerateDataContextOutputs(isDryRun);
    }

    public void GenerateDataContextModels(bool isDryRun)
    {
        modelGenerator.GenerateDataContextModels(isDryRun);
    }

    public void GenerateDataContextStoredProcedures(bool isDryRun)
    {
        storedProcedureGenerator.GenerateDataContextStoredProcedures(isDryRun);
    }
}

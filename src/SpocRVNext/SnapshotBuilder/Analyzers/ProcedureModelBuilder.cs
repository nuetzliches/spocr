using SpocR.SpocRVNext.Models;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Builds <see cref="ProcedureModel"/> instances from SQL definitions.
/// </summary>
internal interface IProcedureModelBuilder
{
    ProcedureModel? Build(string? definition, string? defaultSchema, bool verboseParsing);
}

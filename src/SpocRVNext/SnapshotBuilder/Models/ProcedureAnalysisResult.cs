using SpocR.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

public sealed class ProcedureAnalysisResult
{
    public ProcedureDescriptor Descriptor { get; init; } = new();
    public StoredProcedureContentModel? Ast { get; init; }
    public bool WasReusedFromCache { get; init; }
}

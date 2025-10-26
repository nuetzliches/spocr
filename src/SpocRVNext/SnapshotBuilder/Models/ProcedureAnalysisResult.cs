using System;
using System.Collections.Generic;
using SpocR.DataContext.Models;
using SpocR.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

public sealed class ProcedureAnalysisResult
{
    public ProcedureDescriptor Descriptor { get; init; } = new();
    public StoredProcedureContentModel? Ast { get; init; }
    public bool WasReusedFromCache { get; init; }
    public DateTime? SourceLastModifiedUtc { get; init; }
    public string? SnapshotHash { get; init; }
    public string? SnapshotFile { get; init; }
    public IReadOnlyList<StoredProcedureInput> Parameters { get; init; } = Array.Empty<StoredProcedureInput>();
    public IReadOnlyList<ProcedureDependency> Dependencies { get; init; } = Array.Empty<ProcedureDependency>();
}

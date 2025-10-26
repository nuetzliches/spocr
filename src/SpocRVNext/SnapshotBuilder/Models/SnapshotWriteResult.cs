using System;
using System.Collections.Generic;

namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

public sealed class SnapshotWriteResult
{
    public int FilesWritten { get; init; }
    public int FilesUnchanged { get; init; }
    public IReadOnlyList<ProcedureAnalysisResult> UpdatedProcedures { get; init; } = Array.Empty<ProcedureAnalysisResult>();
}

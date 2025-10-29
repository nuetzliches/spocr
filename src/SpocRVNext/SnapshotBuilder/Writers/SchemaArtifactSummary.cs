using System.Collections.Generic;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

internal sealed class SchemaArtifactSummary
{
    public int FilesWritten { get; set; }
    public int FilesUnchanged { get; set; }
    public List<IndexTableTypeEntry> TableTypes { get; } = new();
    public List<IndexUserDefinedTypeEntry> UserDefinedTypes { get; } = new();
    public List<IndexTableEntry> Tables { get; } = new();
    public int FunctionsVersion { get; set; }
    public List<IndexFunctionEntry> Functions { get; } = new();
}

internal sealed class FunctionArtifactSummary
{
    public int FilesWritten { get; set; }
    public int FilesUnchanged { get; set; }
    public int FunctionsVersion { get; set; }
    public List<IndexFunctionEntry> Functions { get; } = new();
}

internal sealed record FunctionReturnInfo(string? SqlType, int? MaxLength, bool? IsNullable);

internal sealed class TableArtifactSummary
{
    public int FilesWritten { get; set; }
    public int FilesUnchanged { get; set; }
    public List<IndexTableEntry> Tables { get; } = new();
}

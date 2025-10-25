namespace SpocR.SpocRVNext.SnapshotBuilder.Models;

public sealed class SnapshotWriteResult
{
    public int FilesWritten { get; init; }
    public int FilesUnchanged { get; init; }
}

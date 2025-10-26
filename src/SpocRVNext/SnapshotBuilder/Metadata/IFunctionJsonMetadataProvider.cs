using System.Threading;
using System.Threading.Tasks;

namespace SpocR.SpocRVNext.SnapshotBuilder.Metadata;

public interface IFunctionJsonMetadataProvider
{
    Task<FunctionJsonMetadata?> ResolveAsync(string? schema, string name, CancellationToken cancellationToken);
}

public sealed class FunctionJsonMetadata
{
    public FunctionJsonMetadata(bool returnsJson, bool returnsJsonArray, string? rootProperty)
    {
        ReturnsJson = returnsJson;
        ReturnsJsonArray = returnsJsonArray;
        RootProperty = rootProperty;
    }

    public bool ReturnsJson { get; }
    public bool ReturnsJsonArray { get; }
    public string? RootProperty { get; }
}

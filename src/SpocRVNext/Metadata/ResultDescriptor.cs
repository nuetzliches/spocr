namespace SpocR.SpocRVNext.Metadata;

public sealed record ResultDescriptor(
    string OperationName,
    string PayloadType,
    bool HasErrorField = true,
    string? Summary = null,
    string? Remarks = null
);

using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

public sealed record OutputDescriptor(
    string OperationName,
    IReadOnlyList<FieldDescriptor> Fields,
    string? Summary = null,
    string? Remarks = null
);

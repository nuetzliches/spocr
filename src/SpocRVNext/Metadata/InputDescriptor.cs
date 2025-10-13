using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

public sealed record InputDescriptor(
    string OperationName,
    IReadOnlyList<FieldDescriptor> Fields,
    string? Summary = null,
    string? Remarks = null
);

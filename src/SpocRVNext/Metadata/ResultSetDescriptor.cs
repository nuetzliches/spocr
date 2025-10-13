using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

public sealed record ResultSetDescriptor(
    int Index,
    string Name,
    IReadOnlyList<FieldDescriptor> Fields,
    bool IsScalar = false,
    bool Optional = true
);

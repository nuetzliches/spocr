using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

public sealed record FieldDescriptor(
    string Name,
    string PropertyName,
    string ClrType,
    bool IsNullable,
    string SqlTypeName,
    int? MaxLength = null,
    string? Documentation = null,
    IReadOnlyList<string>? Attributes = null
);

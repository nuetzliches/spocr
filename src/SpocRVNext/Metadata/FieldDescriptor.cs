using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

// Erweiterung: Referenz- und Deferred-Flags für JSON Funktions-Expansion zur Generator-Laufzeit.
// Die neuen Properties sind optional; bestehende Call-Sites bleiben kompatibel.
public sealed record FieldDescriptor(
    string Name,
    string PropertyName,
    string ClrType,
    bool IsNullable,
    string SqlTypeName,
    int? MaxLength = null,
    string? Documentation = null,
    IReadOnlyList<string>? Attributes = null,
    ColumnReferenceInfo? Reference = null,
    bool? DeferredJsonExpansion = null
);

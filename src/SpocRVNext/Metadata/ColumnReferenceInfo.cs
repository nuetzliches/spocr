namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// Leichtgewichtiges Referenz-DTO für deferred Expansion (Function/View/Procedure).
/// Spiegelung der Parser-Struktur, bewusst minimal gehalten.
/// </summary>
public sealed record ColumnReferenceInfo(string Kind, string Schema, string Name);

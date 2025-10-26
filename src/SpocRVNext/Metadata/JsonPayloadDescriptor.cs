namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// Describes JSON payload characteristics for result sets and functions.
/// </summary>
public sealed record JsonPayloadDescriptor(bool IsArray, string? RootProperty);

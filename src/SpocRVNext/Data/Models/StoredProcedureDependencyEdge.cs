using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data.Models;

public sealed class StoredProcedureDependencyEdge
{
    [SqlFieldName("referencing_id")]
    public int ReferencingId { get; set; }

    [SqlFieldName("referencing_schema_name")]
    public string ReferencingSchemaName { get; set; } = string.Empty;

    [SqlFieldName("referencing_name")]
    public string ReferencingName { get; set; } = string.Empty;

    [SqlFieldName("referenced_id")]
    public int ReferencedId { get; set; }

    [SqlFieldName("referenced_schema_name")]
    public string ReferencedSchemaName { get; set; } = string.Empty;

    [SqlFieldName("referenced_name")]
    public string ReferencedName { get; set; } = string.Empty;
}

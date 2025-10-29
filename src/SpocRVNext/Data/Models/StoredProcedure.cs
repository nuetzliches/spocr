using System;
using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data.Models;

public sealed class StoredProcedure
{
    [SqlFieldName("object_id")]
    public int Id { get; set; }

    [SqlFieldName("name")]
    public string Name { get; set; } = string.Empty;

    [SqlFieldName("modify_date")]
    public DateTime Modified { get; set; }

    [SqlFieldName("schema_name")]
    public string SchemaName { get; set; } = string.Empty;
}

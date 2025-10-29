using System.Collections.Generic;
using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data.Models;

public sealed class TableType
{
    [SqlFieldName("user_type_id")]
    public int? UserTypeId { get; set; }

    [SqlFieldName("name")]
    public string Name { get; set; } = string.Empty;

    [SqlFieldName("schema_name")]
    public string SchemaName { get; set; } = string.Empty;

    public List<Column> Columns { get; set; } = new();
}

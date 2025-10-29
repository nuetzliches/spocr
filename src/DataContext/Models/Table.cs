using System;
using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models;

public class Table
{
    [SqlFieldName("object_id")]
    public int ObjectId { get; set; }

    [SqlFieldName("schema_name")]
    public string SchemaName { get; set; } = string.Empty;

    [SqlFieldName("table_name")]
    public string Name { get; set; } = string.Empty;

    [SqlFieldName("modify_date")]
    public DateTime ModifyDate { get; set; }
}

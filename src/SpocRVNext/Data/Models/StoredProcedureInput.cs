using System.Collections.Generic;
using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data.Models;

public sealed class StoredProcedureInput : Column
{
    [SqlFieldName("is_output")]
    public bool IsOutput { get; set; }

    [SqlFieldName("is_table_type")]
    public bool IsTableType { get; set; }

    [SqlFieldName("user_type_name")]
    public new string? UserTypeName { get; set; }

    [SqlFieldName("user_type_id")]
    public int? UserTypeId { get; set; }

    [SqlFieldName("user_type_schema_name")]
    public new string? UserTypeSchemaName { get; set; }

    public List<Column> TableTypeColumns { get; set; } = new();

    [SqlFieldName("has_default_value")]
    public bool HasDefaultValue { get; set; }

    [SqlFieldName("base_type_name")]
    public new string? BaseSqlTypeName { get; set; }

    [SqlFieldName("precision")]
    public new int? Precision { get; set; }

    [SqlFieldName("scale")]
    public new int? Scale { get; set; }
}

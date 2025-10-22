using System.Collections.Generic;
using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models;

public class StoredProcedureInput : Column
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
    public List<Column> TableTypeColumns { get; set; }

    [SqlFieldName("has_default_value")]
    public bool HasDefaultValue { get; set; }

    // Zusatzfelder für Normalisierung: base type, precision, scale (kommen über erweiterten Query-Select)
    [SqlFieldName("base_type_name")]
    public new string? BaseSqlTypeName { get; set; }
    [SqlFieldName("precision")]
    public new int? Precision { get; set; }
    [SqlFieldName("scale")]
    public new int? Scale { get; set; }
}

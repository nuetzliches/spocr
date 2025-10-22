using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models;

public class Column
{
    [SqlFieldName("name")]
    public string Name { get; set; }

    [SqlFieldName("is_nullable")]
    public bool IsNullable { get; set; }

    [SqlFieldName("system_type_name")]
    public string SqlTypeName { get; set; }

    [SqlFieldName("max_length")]
    public int MaxLength { get; set; }

    // Neue optionale Felder für erweiterte Abfragen (werden nur gemappt, wenn Query entsprechende Spalten liefert)
    [SqlFieldName("is_identity")]
    public int? IsIdentityRaw { get; set; } // 1/0 von COLUMNPROPERTY; Konvertierung beim Snapshot

    [SqlFieldName("user_type_name")]
    public string? UserTypeName { get; set; }

    [SqlFieldName("user_type_schema_name")]
    public string? UserTypeSchemaName { get; set; }

    [SqlFieldName("base_type_name")]
    public string? BaseSqlTypeName { get; set; }

    [SqlFieldName("precision")]
    public int? Precision { get; set; }

    [SqlFieldName("scale")]
    public int? Scale { get; set; }
}

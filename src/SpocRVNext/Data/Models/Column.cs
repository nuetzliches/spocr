using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data.Models;

public class Column
{
    [SqlFieldName("name")]
    public string Name { get; set; } = string.Empty;

    [SqlFieldName("is_nullable")]
    public bool IsNullable { get; set; }

    [SqlFieldName("system_type_name")]
    public string SqlTypeName { get; set; } = string.Empty;

    [SqlFieldName("max_length")]
    public int MaxLength { get; set; }

    [SqlFieldName("is_identity")]
    public int? IsIdentityRaw { get; set; }

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

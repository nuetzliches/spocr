using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data.Models;

public sealed class StoredProcedureOutput
{
    public string Name { get; set; } = string.Empty;

    [SqlFieldName("is_nullable")]
    public bool IsNullable { get; set; }

    [SqlFieldName("system_type_name")]
    public string SqlTypeName { get; set; } = string.Empty;

    [SqlFieldName("max_length")]
    public int MaxLength { get; set; }

    [SqlFieldName("is_identity_column")]
    public bool IsIdentityColumn { get; set; }

    [SqlFieldName("base_type_name")]
    public string? BaseSqlTypeName { get; set; }

    [SqlFieldName("precision")]
    public int? Precision { get; set; }

    [SqlFieldName("scale")]
    public int? Scale { get; set; }
}

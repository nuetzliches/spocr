using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models;

public class StoredProcedureOutput
{
    public string Name { get; set; }

    [SqlFieldName("is_nullable")]
    public bool IsNullable { get; set; }

    [SqlFieldName("system_type_name")]
    public string SqlTypeName { get; set; }

    [SqlFieldName("max_length")]
    public int MaxLength { get; set; }

    [SqlFieldName("is_identity_column")]
    public bool IsIdentityColumn { get; set; }

    // Erweiterte Felder für Normalisierung (werden nur gemappt wenn Query angepasst wird – aktuell DMV liefert keine alias info direkt)
    [SqlFieldName("base_type_name")]
    public string? BaseSqlTypeName { get; set; }
    [SqlFieldName("precision")]
    public int? Precision { get; set; }
    [SqlFieldName("scale")]
    public int? Scale { get; set; }
}

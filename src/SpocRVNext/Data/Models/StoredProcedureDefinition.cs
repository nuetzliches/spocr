namespace SpocR.SpocRVNext.Data.Models;

public sealed class StoredProcedureDefinition
{
    public string SchemaName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Definition { get; set; } = string.Empty;
}

public sealed class StoredProcedureInputBulk
{
    public string SchemaName { get; set; } = string.Empty;
    public string StoredProcedureName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public string SqlTypeName { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public bool IsOutput { get; set; }
    public bool IsTableType { get; set; }
    public string? UserTypeName { get; set; }
    public int? UserTypeId { get; set; }
    public string? UserTypeSchemaName { get; set; }
}

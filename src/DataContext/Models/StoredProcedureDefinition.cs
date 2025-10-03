using System;

namespace SpocR.DataContext.Models;

public class StoredProcedureDefinition
{
    public string SchemaName { get; set; }
    public string Name { get; set; }
    public int Id { get; set; }
    public string Definition { get; set; }
}

public class StoredProcedureInputBulk
{
    public string SchemaName { get; set; }
    public string StoredProcedureName { get; set; }
    public string Name { get; set; }
    public bool IsNullable { get; set; }
    public string SqlTypeName { get; set; }
    public int MaxLength { get; set; }
    public bool IsOutput { get; set; }
    public bool IsTableType { get; set; }
    public string UserTypeName { get; set; }
    public int? UserTypeId { get; set; }
    public string UserTypeSchemaName { get; set; }
}

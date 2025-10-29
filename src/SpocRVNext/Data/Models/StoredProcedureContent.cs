using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data.Models;

public sealed class StoredProcedureContent
{
    [SqlFieldName("definition")]
    public string Definition { get; set; } = string.Empty;
}

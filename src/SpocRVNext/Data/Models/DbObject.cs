using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data.Models;

public sealed class DbObject
{
    [SqlFieldName("object_id")]
    public int Id { get; set; }
}

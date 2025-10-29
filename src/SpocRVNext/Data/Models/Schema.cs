using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data.Models;

public sealed class Schema
{
    [SqlFieldName("name")]
    public string Name { get; set; } = string.Empty;

    public override string ToString()
    {
        return Name;
    }
}

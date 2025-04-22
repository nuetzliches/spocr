using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models;

public class Schema
{
    // [SqlFieldName("schema_id")]
    // public int Id { get; set; }

    [SqlFieldName("name")]
    public string Name { get; set; }

    public override string ToString()
    {
        return $"{Name}";
    }
}

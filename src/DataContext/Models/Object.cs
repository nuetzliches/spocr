using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models;

// Renamed from Object to DbObject to avoid ambiguity with system 'object'
public class DbObject
{
    [SqlFieldName("object_id")]
    public int Id { get; set; }
}

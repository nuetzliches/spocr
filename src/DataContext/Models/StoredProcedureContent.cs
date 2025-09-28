using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models;

public class StoredProcedureContent
{
    [SqlFieldName("definition")]
    public string Definition { get; set; }
}

using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models
{
    public class Object
    {
        [SqlFieldName("object_id")]
        public int Id { get; set; }
    }
}
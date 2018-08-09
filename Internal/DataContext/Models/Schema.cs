using System;
using SpocR.Internal.DataContext.Attributes;

namespace SpocR.Internal.DataContext.Models
{
    public class Schema
    {
        [SqlFieldName("schema_id")]
        public int Id { get; set; }

        [SqlFieldName("name")]
        public string Name { get; set; }

        public override string ToString() {
            return $"{Id} - {Name}";
        }
    }
}
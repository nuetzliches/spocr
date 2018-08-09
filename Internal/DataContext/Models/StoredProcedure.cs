using System;
using SpocR.Internal.DataContext.Attributes;

namespace SpocR.Internal.DataContext.Models
{
    public class StoredProcedure
    {
        [SqlFieldName("object_id")]
        public int Id { get; set; }
        public string Name { get; set; }
        [SqlFieldName("modify_date")]
        public DateTime Modified { get; set; }

        [SqlFieldName("schema_id")]
        public int SchemaId { get; set; }
    }
}
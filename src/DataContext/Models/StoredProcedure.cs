using System;
using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models
{
    public class StoredProcedure
    {
        // [SqlFieldName("object_id")]
        // public int Id { get; set; }
        public string Name { get; set; }
        [SqlFieldName("modify_date")]
        public DateTime Modified { get; set; }

        [SqlFieldName("schema_name")]
        public string SchemaName { get; set; }
    }
}
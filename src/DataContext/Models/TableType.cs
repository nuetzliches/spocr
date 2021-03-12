using System.Collections.Generic;
using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models
{
    public class TableType
    {
        [SqlFieldName("user_type_id")]
        public int? UserTypeId { get; set; }

        [SqlFieldName("name")]
        public string Name { get; set; }

        [SqlFieldName("schema_name")]
        public string SchemaName { get; set; }

        public List<Column> Columns { get; set; }
    }
}
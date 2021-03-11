using System.Collections.Generic;

namespace SpocR.DataContext.Models
{
    // currently not implemented as DB Query Model
    public class TableType
    {
        public string Name { get; set; }
        public string SchemaName { get; set; }
        public List<ColumnDefinition> Columns { get; set; }
    }
}
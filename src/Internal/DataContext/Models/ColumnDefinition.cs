using System;
using SpocR.Internal.DataContext.Attributes;

namespace SpocR.Internal.DataContext.Models
{
    public class ColumnDefinition
    {
        [SqlFieldName("name")]
        public string Name { get; set; }

        [SqlFieldName("is_nullable")]
        public bool IsNullable { get; set; }

        [SqlFieldName("system_type_name")]
        public string SqlTypeName { get; set; }
        
        [SqlFieldName("max_length")]
        public int MaxLength { get; set; }
    }
}
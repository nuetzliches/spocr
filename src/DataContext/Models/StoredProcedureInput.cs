using System;
using System.Collections.Generic;
using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models
{
    public class StoredProcedureInput : ColumnDefinition
    {
        [SqlFieldName("is_output")]
        public bool IsOutput { get; set; }

        [SqlFieldName("is_table_type")]
        public bool IsTableType { get; set; }

        [SqlFieldName("user_type_name")]
        public string UserTypeName { get; set; }

        [SqlFieldName("user_type_id")]
        public int? UserTypeId { get; set; }

        [SqlFieldName("user_type_schema_name")]
        public string UserTypeSchemaName { get; set; }
        public List<ColumnDefinition> TableTypeColumns { get; set; }
    }
}
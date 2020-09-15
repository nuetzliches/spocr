using System;
using SpocR.DataContext.Attributes;

namespace SpocR.DataContext.Models
{
    public class StoredProcedureOutput
    {
        public string Name { get; set; }

        [SqlFieldName("is_nullable")]
        public bool IsNullable { get; set; }

        [SqlFieldName("system_type_name")]
        public string SqlTypeName { get; set; }

        private int _maxLength { get; set; }

        [SqlFieldName("max_length")]
        public int MaxLength
        {
            // see: https://www.sqlservercentral.com/forums/topic/sql-server-max_lenght-returns-double-the-actual-size#unicode
            get => SqlTypeName.StartsWith("nvarchar") ? _maxLength / 2 : _maxLength;
            set => this._maxLength = value;
        }

        [SqlFieldName("is_identity_column")]
        public bool IsIdentityColumn { get; set; }
    }
}
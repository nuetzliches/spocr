using System.Collections.Generic;
using System.Linq;
using SpocR.DataContext.Models;

namespace SpocR.Models
{
    public class StoredProcedureInputModel : ColumnModel
    {
        private readonly StoredProcedureInput _item;
        public StoredProcedureInputModel(StoredProcedureInput item)
            : base(item)
        {
            _item = item ?? new StoredProcedureInput();
        }

        public bool? IsTableType
        {
            get => _item.IsTableType ? (bool?)true : null;
            set => _item.IsTableType = value == true ? true : false;
        }

        public List<ColumnModel> Columns => _item.TableTypeColumns?
            .Select(i => new ColumnModel(i))
            .ToList();
    }

    public class ColumnModel
    {
        private readonly StoredProcedureInput _item;

        public ColumnModel()
        {
            // required for JSON Serializer
            _item = new StoredProcedureInput();
        }

        public ColumnModel(StoredProcedureInput item)
        {
            _item = item ?? new StoredProcedureInput();
        }

        public ColumnModel(ColumnDefinition column)
        {
            _item = column != null ? new StoredProcedureInput
            {
                Name = column.Name,
                IsNullable = column.IsNullable,
                SqlTypeName = column.SqlTypeName,
                MaxLength = column.MaxLength
            } : new StoredProcedureInput();
        }

        public string Name
        {
            get => _item.Name;
            set => _item.Name = value;
        }

        public bool? IsNullable
        {
            get => _item.IsNullable ? (bool?)true : null;
            set => _item.IsNullable = value == true ? true : false;
        }
        
        public string SqlTypeName
        {
            get => _item.SqlTypeName;
            set => _item.SqlTypeName = value;
        }
        public int? MaxLength
        {
            get => _item.MaxLength > 0 ? (int?)_item.MaxLength : null;
            set => _item.MaxLength = (int)(value > 0 ? value : 0);
        }
        // public bool IsOutput
        // {
        //     get => _item.IsOutput;
        //     set => _item.IsOutput = value;
        // }
    }
}
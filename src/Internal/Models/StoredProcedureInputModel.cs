using System;
using SpocR.Internal.DataContext.Models;

namespace SpocR.Internal.Models
{
    public class StoredProcedureInputModel
    {
        private readonly StoredProcedureInput _item;
        public StoredProcedureInputModel(StoredProcedureInput item)
        {
            _item = item ?? new StoredProcedureInput();
        }

        public string Name
        {
            get => _item.Name;
            set => _item.Name = value;
        }
        public bool IsNullable
        {
            get => _item.IsNullable;
            set => _item.IsNullable = value;
        }
        public string SqlTypeName
        {
            get => _item.SqlTypeName;
            set => _item.SqlTypeName = value;
        }
        public int MaxLength
        {
            get => _item.MaxLength;
            set => _item.MaxLength = value;
        }
        // public bool IsOutput
        // {
        //     get => _item.IsOutput;
        //     set => _item.IsOutput = value;
        // }
    }
}
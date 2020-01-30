using SpocR.DataContext.Models;

namespace SpocR.Models
{
    public class StoredProcedureOutputModel
    {
        private readonly StoredProcedureOutput _item;
        public StoredProcedureOutputModel(StoredProcedureOutput item)
        {
            _item = item ?? new StoredProcedureOutput();
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

        public int MaxLength
        {
            get => _item.MaxLength;
            set => _item.MaxLength = value;
        }
    }
}
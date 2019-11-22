using System.Collections.Generic;
using SpocR.DataContext.Models;

namespace SpocR.Models
{
    public class StoredProcedureModel
    {
        private readonly StoredProcedure _item;
        public StoredProcedureModel(StoredProcedure item = null)
        {
            _item = item ?? new StoredProcedure();
        }

        public int Id
        {
            get => _item.Id;
            set => _item.Id = value;
        }
        public string Name
        {
            get => _item.Name;
            set => _item.Name = value;
        }

        public int SchemaId
        {
            get => _item.SchemaId;
            set => _item.SchemaId = value;
        }

        public IEnumerable<StoredProcedureInputModel> Input { get; set; }
        public IEnumerable<StoredProcedureOutputModel> Output { get; set; }
    }
}
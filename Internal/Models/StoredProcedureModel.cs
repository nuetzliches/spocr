using System;
using System.Collections.Generic;
using SpocR.Internal.DataContext.Models;

namespace SpocR.Internal.Models
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
        public DateTime Modified
        {
            get => _item.Modified;
            set => _item.Modified = value;
        }

        public IEnumerable<StoredProcedureInputModel> Input { get; set; }
        public IEnumerable<StoredProcedureOutputModel> Output { get; set; }
    }
}
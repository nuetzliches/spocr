using System;
using System.Collections.Generic;
using SpocR.Internal.DataContext.Models;

namespace SpocR.Internal.Models
{
    public class SchemaModel
    {
        private readonly Schema _item;

        public SchemaModel(Schema item = null)
        {
            _item = item ?? new Schema();
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
        
        public IEnumerable<StoredProcedureModel> StoredProcedures { get; set; }
    }
}
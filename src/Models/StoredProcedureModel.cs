using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SpocR.DataContext.Models;

namespace SpocR.Models
{
    public class StoredProcedureModel : IEquatable<StoredProcedureModel>
    {
        private readonly StoredProcedure _item;
        public StoredProcedureModel(StoredProcedure item = null)
        {
            _item = item ?? new StoredProcedure();
        }

        // public int Id
        // {
        //     get => _item.Id;
        //     set => _item.Id = value;
        // }
        
        public string Name
        {
            get => _item.Name;
            set => _item.Name = value;
        }

        [JsonIgnore]
        public string SchemaName
        {
            get => _item.SchemaName;
            set => _item.SchemaName = value;
        }

        public IEnumerable<StoredProcedureInputModel> Input { get; set; }
        public IEnumerable<StoredProcedureOutputModel> Output { get; set; }

        public bool Equals(StoredProcedureModel other)
        {
            return SchemaName == other.SchemaName && Name == other.Name;
        }

        public override string ToString() {
            return $"[SchemaName].[Name]";
        }
    }
}
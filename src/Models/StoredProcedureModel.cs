using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using SpocR.DataContext.Models;

namespace SpocR.Models
{
    public class StoredProcedureModel : IEquatable<StoredProcedureModel>
    {
        private readonly StoredProcedure _item;

        public StoredProcedureModel() // required for json serialization
        {
            _item = new StoredProcedure();
        }

        public StoredProcedureModel(StoredProcedure item)
        {
            _item = item;
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

        private IEnumerable<StoredProcedureInputModel> _input;
        public IEnumerable<StoredProcedureInputModel> Input
        {
            get => _input?.Any() ?? false ? _input : null;
            set => _input = value;
        }

        private IEnumerable<StoredProcedureOutputModel> _output;
        public IEnumerable<StoredProcedureOutputModel> Output
        {
            get => _output?.Any() ?? false ? _output : null;
            set => _output = value;
        }

        // public IEnumerable<StoredProcedureInputModel> Input { get; set; }
        // public IEnumerable<StoredProcedureOutputModel> Output { get; set; }

        public bool Equals(StoredProcedureModel other)
        {
            return SchemaName == other.SchemaName && Name == other.Name;
        }

        public override string ToString()
        {
            return $"[SchemaName].[Name]";
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using SpocR.DataContext.Models;

namespace SpocR.Models
{
    public class TableTypeModel : IEquatable<TableTypeModel>
    {
        private readonly TableType _item;

        public TableTypeModel() // required for json serialization
        {
            _item = new TableType();
        }

        public TableTypeModel(StoredProcedureInputModel item, List<ColumnDefinition> columns)
        {
            _item = new TableType
            {
                Name = item.TableTypeName,
                SchemaName = item.TableTypeSchemaName
            };
            Columns = columns.Select(c => new ColumnModel(c)).ToList();
        }

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

        public List<ColumnModel> _columns;
        public List<ColumnModel> Columns
        {
            get { return _columns; }
            set { _columns = value; }
        }

        public bool Equals(TableTypeModel other)
        {
            return SchemaName == other.SchemaName && Name == other.Name;
        }

        public override string ToString()
        {
            return $"[SchemaName].[Name]";
        }
    }
}
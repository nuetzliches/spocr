using System;
using System.Collections.Generic;
using System.Linq;
using SpocR.Extensions;
using SpocR.Models;

namespace SpocR.SpocRVNext.Core;

public static class Definition
{
    public static Schema ForSchema(SchemaModel schema)
    {
        return new Schema(schema);
    }

    public static StoredProcedure ForStoredProcedure(StoredProcedureModel storedProcedure, Schema schema)
    {
        return new StoredProcedure(storedProcedure, schema);
    }

    public static TableType ForTableType(TableTypeModel tableType, Schema schema)
    {
        return new TableType(tableType, schema);
    }

    public class Schema
    {
        private readonly SchemaModel _schema;

        public Schema(SchemaModel schema)
        {
            _schema = schema;
            Identifier = schema.Name;
            Name = schema.Name.ToPascalCase();
            Path = Name;
        }

        public string Identifier { get; }
        public string Name { get; }
        public string Path { get; }

        private IEnumerable<StoredProcedure> _storedProcedures;
        public IEnumerable<StoredProcedure> StoredProcedures
            => _storedProcedures ??= _schema.StoredProcedures?.Select(i => ForStoredProcedure(i, this));

        private IEnumerable<TableType> _tableTypes;
        public IEnumerable<TableType> TableTypes
            => _tableTypes ??= _schema.TableTypes?.Select(i => ForTableType(i, this));
    }

    public class StoredProcedure(StoredProcedureModel storedProcedure, Schema schema)
    {
        private string _sqlObjectName;
        private string _name;

        public string SqlObjectName => _sqlObjectName ??= $"[{schema.Identifier}].[{Name}]";

        public string Name => _name ??= storedProcedure.Name;

        public IReadOnlyList<StoredProcedureContentModel.ResultSet> ResultSets => storedProcedure.ResultSets;

        public IEnumerable<StoredProcedureInputModel> Input => storedProcedure.Input ?? [];

        public bool IsPureWrapper
        {
            get
            {
                var sets = storedProcedure.ResultSets;
                if (sets == null || sets.Count != 1) return false;
                var rs = sets[0];
                bool hasExec = !string.IsNullOrEmpty(rs.ExecSourceProcedureName);
                bool noCols = rs.Columns == null || rs.Columns.Count == 0;
                bool notJson = !rs.ReturnsJson && !rs.ReturnsJsonArray;
                return hasExec && noCols && notJson;
            }
        }
    }

    public class TableType(TableTypeModel tableType, Schema schema)
    {
        private string _sqlObjectName;
        private string _name;

        public string SqlObjectName => _sqlObjectName ??= $"[{schema.Name.ToLower()}].[{Name}]";

        public string Name => _name ??= tableType.Name;

        public IEnumerable<ColumnModel> Columns => tableType.Columns ?? [];
    }
}

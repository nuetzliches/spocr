using System;
using System.Collections.Generic;
using System.Linq;
using SpocR.Extensions;
using SpocR.Models;

namespace SpocR.Contracts;

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

        //
        // Returns:
        //     The sql object name of the StoredProcedure
        public string SqlObjectName => _sqlObjectName ??= $"[{schema.Identifier}].[{Name}]";

        //
        // Returns:
        //     The FullName of the StoredProcedure
        public string Name => _name ??= storedProcedure.Name;

        //
        // Returns:
        //     The part of the Name before the [Operation] starts. 
        //     e.g.: "User" from Name "UserCreate"
        // Removed obsolete OperationKind/ReadWriteKind/ResultKind logic.
        // If similar semantics are needed in future, derive externally from ResultSets / parse flags.
        // Expose only raw ResultSets; callers must inspect sets explicitly (no flattened convenience properties)
        public IReadOnlyList<StoredProcedureContentModel.ResultSet> ResultSets => storedProcedure.ResultSets;

        public IEnumerable<StoredProcedureInputModel> Input => storedProcedure.Input ?? [];
    }

    public class TableType(TableTypeModel tableType, Schema schema)
    {
        private string _sqlObjectName;
        private string _name;

        //
        // Returns:
        //     The sql object name of the TableType
        public string SqlObjectName => _sqlObjectName ??= $"[{schema.Name.ToLower()}].[{Name}]";

        //
        // Returns:
        //     The FullName of the TableType
        public string Name => _name ??= tableType.Name;

        public IEnumerable<ColumnModel> Columns => tableType.Columns ?? [];
    }
}


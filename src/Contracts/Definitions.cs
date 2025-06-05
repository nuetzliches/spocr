using System;
using System.Collections.Generic;
using System.Linq;
using SpocR.Extensions;
using SpocR.Models;

namespace SpocR.Contracts;

public static class Definition
{
    public enum OperationKindEnum
    {
        Undefined,
        Create,
        Update,
        Delete,
        Merge,
        Upsert,
        Find,
        List
    }

    public enum ReadWriteKindEnum
    {
        Undefined,
        Read,
        Write
    }

    public enum ResultKindEnum
    {
        Undefined,
        Single,
        List
    }

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
            Name = schema.Name.ToLower().ToPascalCase();
            Path = Name;
        }

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
        private string _entityName;
        private string _suffix;
        private OperationKindEnum _operationKind;
        private ReadWriteKindEnum _readWriteKind;
        private ResultKindEnum _resultKind;

        //
        // Returns:
        //     The sql object name of the StoredProcedure
        public string SqlObjectName => _sqlObjectName ??= $"[{schema.Name.ToLower()}].[{Name}]";

        //
        // Returns:
        //     The FullName of the StoredProcedure
        public string Name => _name ??= storedProcedure.Name;

        //
        // Returns:
        //     The part of the Name before the [Operation] starts. 
        //     e.g.: "User" from Name "UserCreate"
        public string EntityName => _entityName ??= OperationKind != OperationKindEnum.Undefined
                ? Name[..Name.IndexOf(OperationKind.ToString())]
                : Name
;
        public string Suffix => _suffix ??= Name[(Name.IndexOf(OperationKind.ToString()) + OperationKind.ToString().Length)..];
        public OperationKindEnum OperationKind => _operationKind != OperationKindEnum.Undefined
            ? _operationKind
            : (_operationKind = Enum.GetValues<OperationKindEnum>()
                .Select(kind => new { Kind = kind, Index = Name.IndexOf(kind.ToString()) })
                .Where(x => x.Index >= 0)
                .OrderBy(x => x.Index)
                .FirstOrDefault()?.Kind ?? OperationKindEnum.Undefined);
        public ReadWriteKindEnum ReadWriteKind => _readWriteKind != ReadWriteKindEnum.Undefined
            ? _readWriteKind
            : _readWriteKind = new[] { OperationKindEnum.Find, OperationKindEnum.List }.Contains(OperationKind)
                        ? ReadWriteKindEnum.Read
                        : ReadWriteKindEnum.Write;
        public ResultKindEnum ResultKind => _resultKind != ResultKindEnum.Undefined
            ? _resultKind
            : (_resultKind = OperationKind == OperationKindEnum.List || Name.Contains("WithChildren")
                ? ResultKindEnum.List
                : ResultKindEnum.Single);

        public IEnumerable<StoredProcedureInputModel> Input => storedProcedure.Input ?? [];
        public IEnumerable<StoredProcedureOutputModel> Output => storedProcedure.Output ?? [];
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

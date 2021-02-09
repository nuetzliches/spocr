using System;
using System.Collections.Generic;
using System.Linq;
using SpocR.Models;

namespace SpocR.Contracts
{
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

        public class Schema
        {
            private readonly SchemaModel _schema;

            public Schema(SchemaModel schema)
            {
                _schema = schema;
                var name = schema.Name.ToLower();
                Name = name.First().ToString().ToUpper() + name.Substring(1);
                Path = Name;
            }

            public string Name { get; }
            public string Path { get; }

            private IEnumerable<StoredProcedure> _storedProcedures;
            public IEnumerable<StoredProcedure> StoredProcedures
                => _storedProcedures ?? (_storedProcedures = _schema.StoredProcedures.Select(i => ForStoredProcedure(i, this)));
        }

        public class StoredProcedure
        {
            private readonly StoredProcedureModel _storedProcedure;
            private readonly Schema _schema;
            private string _sqlObjectName;
            private string _name;
            private string _entityName;
            private string _suffix;
            private OperationKindEnum _operationKind;
            private ReadWriteKindEnum _readWriteKind;
            private ResultKindEnum _resultKind;

            public StoredProcedure(StoredProcedureModel storedProcedure, Schema schema)
            {
                _storedProcedure = storedProcedure;
                _schema = schema;
            }

            //
            // Returns:
            //     The sql object name of the StoredProcedure
            public string SqlObjectName => _sqlObjectName ?? (_sqlObjectName = $"[{_schema.Name.ToLower()}].[{Name}]");

            //
            // Returns:
            //     The FullName of the StoredProcedure
            public string Name => _name ?? (_name = _storedProcedure.Name);

            //
            // Returns:
            //     The part of the Name before the [Operation] starts. 
            //     e.g.: "User" from Name "UserCreate"
            public string EntityName => _entityName
                ?? (_entityName = OperationKind != OperationKindEnum.Undefined
                    ? Name.Substring(0, Name.IndexOf(OperationKind.ToString()))
                    : Name
            );
            public string Suffix => _suffix ?? (_suffix = Name.Substring(Name.IndexOf(OperationKind.ToString()) + OperationKind.ToString().Length));
            public OperationKindEnum OperationKind => _operationKind != OperationKindEnum.Undefined
                ? _operationKind
                : (_operationKind = ((OperationKindEnum[])Enum.GetValues(typeof(OperationKindEnum)))
                    .FirstOrDefault(i => Name.Contains(i.ToString())));
            public ReadWriteKindEnum ReadWriteKind => _readWriteKind != ReadWriteKindEnum.Undefined
                ? _readWriteKind
                : _readWriteKind = (new[] { OperationKindEnum.Find, OperationKindEnum.List }.Contains(OperationKind)
                            ? ReadWriteKindEnum.Read
                            : ReadWriteKindEnum.Write);
            public ResultKindEnum ResultKind => _resultKind != ResultKindEnum.Undefined
                ? _resultKind
                : (_resultKind = (OperationKind == OperationKindEnum.List || Name.Contains("WithChildren")
                    ? ResultKindEnum.List
                    : ResultKindEnum.Single));

            public IEnumerable<StoredProcedureInputModel> Input => _storedProcedure.Input;
            public IEnumerable<StoredProcedureOutputModel> Output => _storedProcedure.Output;
        }
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using SpocR.Internal.Models;

namespace SpocR.Internal.Common
{
    internal static class Definitions
    {
        internal enum OperationKindEnum
        {
            Undefined,
            Create,
            Update,
            Delete,
            Merge,
            Upsert,
            FindBy,
            List
        }

        internal enum ReadWriteKindEnum
        {
            Undefined,
            Read,
            Write
        }

        internal enum ResultKindEnum
        {
            Undefined,
            Single,
            List
        }

        internal static SchemaDefinition ForSchema(SchemaModel schema)
        {
            return new SchemaDefinition(schema);
        }

        internal static StoredProcedureDefinition ForStoredProcedure(StoredProcedureModel storedProcedure, SchemaDefinition schema)
        {
            return new StoredProcedureDefinition(storedProcedure, schema);
        }

        internal class SchemaDefinition
        {
            private readonly SchemaModel _schema;

            internal SchemaDefinition(SchemaModel schema)
            {
                _schema = schema;
                var name = schema.Name.ToLower();
                Name = name.First().ToString().ToUpper() + name.Substring(1);
                Path = Name;
            }

            internal string Name { get; }
            internal string Path { get; }

            private IEnumerable<StoredProcedureDefinition> _storedProcedures;
            internal IEnumerable<StoredProcedureDefinition> StoredProcedures
                => _storedProcedures ?? (_storedProcedures = _schema.StoredProcedures.Select(i => Definitions.ForStoredProcedure(i, this)));
        }

        internal class StoredProcedureDefinition
        {
            private readonly StoredProcedureModel _storedProcedure;
            private readonly SchemaDefinition _schema;
            private string _sqlObjectName;
            private string _name;
            private string _entityName;
            private string _suffix;
            private OperationKindEnum _operationKind;
            private ReadWriteKindEnum _readWriteKind;
            private ResultKindEnum _resultKind;

            internal StoredProcedureDefinition(StoredProcedureModel storedProcedure, SchemaDefinition schema)
            {
                _storedProcedure = storedProcedure;
                _schema = schema;
            }

            //
            // Returns:
            //     The sql object name of the StoredProcedure
            internal string SqlObjectName => _sqlObjectName ?? (_sqlObjectName = $"[{_schema.Name.ToLower()}].[{Name}]");

            //
            // Returns:
            //     The FullName of the StoredProcedure
            internal string Name => _name ?? (_name = _storedProcedure.Name);

            //
            // Returns:
            //     The part of the Name before the [Operation] starts. 
            //     e.g.: "User" from Name "UserCreate"
            internal string EntityName => _entityName ?? (_entityName = Name.Substring(0, Name.IndexOf(OperationKind.ToString())));
            internal string Suffix => _suffix ?? (_suffix = Name.Substring(Name.IndexOf(OperationKind.ToString()) + OperationKind.ToString().Length));
            internal OperationKindEnum OperationKind => _operationKind != OperationKindEnum.Undefined
                ? _operationKind
                : (_operationKind = ((OperationKindEnum[])Enum.GetValues(typeof(OperationKindEnum)))
                    .FirstOrDefault(i => Name.Contains(i.ToString())));
            internal ReadWriteKindEnum ReadWriteKind => _readWriteKind != ReadWriteKindEnum.Undefined
                ? _readWriteKind
                : _readWriteKind = (new[] { OperationKindEnum.FindBy, OperationKindEnum.List }.Contains(OperationKind)
                            ? ReadWriteKindEnum.Read
                            : ReadWriteKindEnum.Write);
            internal ResultKindEnum ResultKind => _resultKind != ResultKindEnum.Undefined
                ? _resultKind
                : (_resultKind = (OperationKind == OperationKindEnum.List || Name.Contains("WithChildren") 
                    ? ResultKindEnum.List 
                    : ResultKindEnum.Single));

            internal IEnumerable<StoredProcedureInputModel> Input => _storedProcedure.Input;
            internal IEnumerable<StoredProcedureOutputModel> Output => _storedProcedure.Output;
        }
    }
}


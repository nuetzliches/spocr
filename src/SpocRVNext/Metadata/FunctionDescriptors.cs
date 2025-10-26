using System;
using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// Minimaler Funktions-Descriptor (vNext Phase 1): spiegelt schlanken Snapshot ohne Definition/Hash wider.
/// Für Table-Valued Functions ist <see cref="ReturnSqlType"/> leer/Null und Spalten stehen in <see cref="Columns"/>.
/// </summary>
public sealed record FunctionDescriptor(
    string SchemaName,
    string FunctionName,
    bool IsTableValued,
    string? ReturnSqlType,
    int? ReturnMaxLength,
    bool? ReturnIsNullable,
    JsonPayloadDescriptor? JsonPayload,
    bool IsEncrypted,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<FunctionParameterDescriptor> Parameters,
    IReadOnlyList<TableValuedFunctionColumnDescriptor> Columns
);

/// <summary>
/// Describes a function parameter including CLR mapping information.
/// </summary>
public sealed record FunctionParameterDescriptor(
    string Name,
    string SqlType,
    string ClrType,
    bool IsNullable,
    int? MaxLength,
    bool IsOutput
);

/// <summary>
/// Describes a single column of the row returned by a table-valued function.
/// </summary>
public sealed record TableValuedFunctionColumnDescriptor(
    string Name,
    string SqlType,
    string ClrType,
    bool IsNullable,
    int? MaxLength
);

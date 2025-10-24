using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

public sealed record ResultSetDescriptor(
    int Index,
    string Name,
    IReadOnlyList<FieldDescriptor> Fields,
    bool IsScalar = false,
    bool Optional = true,
    bool HasSelectStar = false,
    string? ExecSourceSchemaName = null,
    string? ExecSourceProcedureName = null,
    bool ReturnsJson = false,
    bool ReturnsJsonArray = false,
    ColumnReferenceInfo? Reference = null // Konsolidierung: ExecSource* -> Reference.Kind="Procedure"
);

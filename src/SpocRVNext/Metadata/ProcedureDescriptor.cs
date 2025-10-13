using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

public sealed record ProcedureDescriptor(
    string ProcedureName,
    string Schema,
    string OperationName,
    IReadOnlyList<FieldDescriptor> InputParameters,
    IReadOnlyList<FieldDescriptor> OutputFields,
    IReadOnlyList<ResultSetDescriptor> ResultSets,
    string? Summary = null,
    string? Remarks = null
);

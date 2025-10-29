using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpocR.SpocRVNext.Data.Models;
using SpocR.SpocRVNext.SnapshotBuilder.Metadata;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

internal static class ProcedureSnapshotDocumentBuilder
{
    internal static byte[] BuildProcedureJson(
        ProcedureDescriptor descriptor,
        IReadOnlyList<StoredProcedureInput> parameters,
        ProcedureModel? procedure,
        ISet<string>? requiredTypeRefs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", descriptor?.Schema ?? string.Empty);
            writer.WriteString("Name", descriptor?.Name ?? string.Empty);

            WriteParameters(writer, parameters, requiredTypeRefs);
            WriteResultSets(writer, procedure?.ResultSets, requiredTypeRefs);

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteParameters(Utf8JsonWriter writer, IReadOnlyList<StoredProcedureInput> parameters, ISet<string>? requiredTypeRefs)
    {
        writer.WritePropertyName("Parameters");
        writer.WriteStartArray();
        if (parameters != null)
        {
            foreach (var input in parameters)
            {
                if (input == null)
                {
                    continue;
                }

                writer.WriteStartObject();
                var name = SnapshotWriterUtilities.NormalizeParameterName(input.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    writer.WriteString("Name", name);
                }

                var typeRef = SnapshotWriterUtilities.BuildTypeRef(input);
                if (!string.IsNullOrWhiteSpace(typeRef))
                {
                    writer.WriteString("TypeRef", typeRef);
                    SnapshotWriterUtilities.RegisterTypeRef(requiredTypeRefs, typeRef);
                }

                if (!input.IsTableType)
                {
                    if (SnapshotWriterUtilities.ShouldEmitIsNullable(input.IsNullable, typeRef))
                    {
                        writer.WriteBoolean("IsNullable", true);
                    }

                    if (SnapshotWriterUtilities.ShouldEmitMaxLength(input.MaxLength, typeRef))
                    {
                        writer.WriteNumber("MaxLength", input.MaxLength);
                    }

                    var precision = input.Precision;
                    if (SnapshotWriterUtilities.ShouldEmitPrecision(precision, typeRef))
                    {
                        writer.WriteNumber("Precision", precision.GetValueOrDefault());
                    }

                    var scale = input.Scale;
                    if (SnapshotWriterUtilities.ShouldEmitScale(scale, typeRef))
                    {
                        writer.WriteNumber("Scale", scale.GetValueOrDefault());
                    }
                }

                if (input.IsOutput)
                {
                    writer.WriteBoolean("IsOutput", true);
                }

                if (input.HasDefaultValue)
                {
                    writer.WriteBoolean("HasDefaultValue", true);
                }

                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteResultSets(Utf8JsonWriter writer, IReadOnlyList<ProcedureResultSet>? resultSets, ISet<string>? requiredTypeRefs)
    {
        writer.WritePropertyName("ResultSets");
        writer.WriteStartArray();
        if (resultSets != null)
        {
            foreach (var set in resultSets)
            {
                if (set == null || !ShouldIncludeResultSet(set))
                {
                    continue;
                }

                writer.WriteStartObject();
                if (set.ReturnsJson)
                {
                    writer.WriteBoolean("ReturnsJson", true);
                }

                if (set.ReturnsJsonArray)
                {
                    writer.WriteBoolean("ReturnsJsonArray", true);
                }

                if (!string.IsNullOrWhiteSpace(set.JsonRootProperty))
                {
                    writer.WriteString("JsonRootProperty", set.JsonRootProperty);
                }

                if (!string.IsNullOrWhiteSpace(set.ExecSourceSchemaName))
                {
                    writer.WriteString("ExecSourceSchemaName", set.ExecSourceSchemaName);
                }

                if (!string.IsNullOrWhiteSpace(set.ExecSourceProcedureName))
                {
                    writer.WriteString("ExecSourceProcedureName", set.ExecSourceProcedureName);
                }

                var procedureRef = BuildProcedureRef(set);
                if (!string.IsNullOrWhiteSpace(procedureRef))
                {
                    writer.WriteString("ProcedureRef", procedureRef);
                }

                if (set.HasSelectStar)
                {
                    writer.WriteBoolean("HasSelectStar", true);
                }

                writer.WritePropertyName("Columns");
                writer.WriteStartArray();
                if (set.Columns != null)
                {
                    foreach (var column in set.Columns)
                    {
                        if (column == null)
                        {
                            continue;
                        }

                        WriteResultColumn(writer, column, requiredTypeRefs);
                    }
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteResultColumn(Utf8JsonWriter writer, ProcedureResultColumn column, ISet<string>? requiredTypeRefs)
    {
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(column.Name))
        {
            writer.WriteString("Name", column.Name);
        }

        var typeRef = SnapshotWriterUtilities.BuildTypeRef(column);
        if (!string.IsNullOrWhiteSpace(typeRef) && column.ReturnsJson != true && column.IsNestedJson != true)
        {
            writer.WriteString("TypeRef", typeRef);
            SnapshotWriterUtilities.RegisterTypeRef(requiredTypeRefs, typeRef);
        }

        if (column.IsNestedJson == true && column.ReturnsJson != true)
        {
            writer.WriteBoolean("IsNestedJson", true);
        }

        if (column.ReturnsJson == true)
        {
            writer.WriteBoolean("ReturnsJson", true);
        }

        if (column.ReturnsJsonArray == true)
        {
            writer.WriteBoolean("ReturnsJsonArray", true);
        }

        if (!string.IsNullOrWhiteSpace(column.JsonRootProperty))
        {
            writer.WriteString("JsonRootProperty", column.JsonRootProperty);
        }

        var sqlTypeName = DeriveSqlTypeName(column, typeRef);
        if (!string.IsNullOrWhiteSpace(sqlTypeName))
        {
            writer.WriteString("SqlTypeName", sqlTypeName);
        }

        if (SnapshotWriterUtilities.ShouldEmitIsNullable(column.IsNullable, typeRef))
        {
            writer.WriteBoolean("IsNullable", true);
        }

        var columnMaxLength = column.MaxLength ?? column.CastTargetLength;
        if (SnapshotWriterUtilities.ShouldEmitMaxLength(columnMaxLength, typeRef))
        {
            writer.WriteNumber("MaxLength", columnMaxLength.GetValueOrDefault());
        }

        if (column.Columns != null && column.Columns.Count > 0)
        {
            writer.WritePropertyName("Columns");
            writer.WriteStartArray();
            foreach (var child in column.Columns)
            {
                if (child == null)
                {
                    continue;
                }

                WriteResultColumn(writer, child, requiredTypeRefs);
            }

            writer.WriteEndArray();
        }

        var functionRef = BuildFunctionRef(column);
        if (!string.IsNullOrWhiteSpace(functionRef))
        {
            writer.WriteString("FunctionRef", functionRef);
        }

        if (column.DeferredJsonExpansion == true)
        {
            writer.WriteBoolean("DeferredJsonExpansion", true);
        }

        writer.WriteEndObject();
    }

    private static string? DeriveSqlTypeName(ProcedureResultColumn column, string? typeRef)
    {
        if (column == null)
        {
            return null;
        }

        if (column.ReturnsJson == true || column.IsNestedJson == true)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(typeRef))
        {
            return null;
        }

        static string? NormalizeCandidate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return SnapshotWriterUtilities.NormalizeSqlTypeName(raw);
        }

        var candidate = NormalizeCandidate(column.SqlTypeName);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        candidate = NormalizeCandidate(column.CastTargetType);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        var aggregate = column.AggregateFunction;
        if (string.IsNullOrWhiteSpace(aggregate))
        {
            aggregate = TryExtractFunctionName(column.RawExpression);
        }

        if (!string.IsNullOrWhiteSpace(aggregate))
        {
            switch (aggregate.Trim().ToLowerInvariant())
            {
                case "count":
                    return "int";
                case "count_big":
                    return "bigint";
                case "sum":
                case "avg":
                    return "decimal(18,2)";
                case "min":
                case "max":
                    if (column.HasIntegerLiteral)
                    {
                        return "int";
                    }

                    if (column.HasDecimalLiteral)
                    {
                        return "decimal(18,2)";
                    }

                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(column.RawExpression))
        {
            var raw = column.RawExpression.Trim();
            if (raw.StartsWith("EXISTS", StringComparison.OrdinalIgnoreCase))
            {
                return "bit";
            }

            if (LooksLikeBooleanCase(raw))
            {
                return "bit";
            }
        }

        return null;
    }

    private static bool LooksLikeBooleanCase(string rawExpression)
    {
        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            return false;
        }

        if (!rawExpression.StartsWith("CASE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        static bool ContainsPattern(string source, string pattern)
            => source.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

        var hasThenOneElseZero = ContainsPattern(rawExpression, " THEN 1") && ContainsPattern(rawExpression, " ELSE 0");
        var hasThenZeroElseOne = ContainsPattern(rawExpression, " THEN 0") && ContainsPattern(rawExpression, " ELSE 1");
        return hasThenOneElseZero || hasThenZeroElseOne;
    }

    private static string? TryExtractFunctionName(string? rawExpression)
    {
        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            return null;
        }

        var match = Regex.Match(rawExpression, "^\\s*([A-Za-z0-9_]+)\\s*\\(");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool ShouldIncludeResultSet(ProcedureResultSet set)
    {
        if (set == null)
        {
            return false;
        }

        if (set.ReturnsJson || set.ReturnsJsonArray)
        {
            return true;
        }

        if (set.Columns != null && set.Columns.Count > 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(set.ExecSourceProcedureName))
        {
            return true;
        }

        return false;
    }

    private static string? BuildProcedureRef(ProcedureResultSet set)
    {
        if (set == null)
        {
            return null;
        }

        if (set.Reference != null && string.Equals(set.Reference.Kind, "Procedure", StringComparison.OrdinalIgnoreCase))
        {
            return SnapshotWriterUtilities.ComposeSchemaObjectRef(set.Reference.Schema, set.Reference.Name);
        }

        if (!string.IsNullOrWhiteSpace(set.ExecSourceProcedureName))
        {
            return SnapshotWriterUtilities.ComposeSchemaObjectRef(set.ExecSourceSchemaName, set.ExecSourceProcedureName);
        }

        return null;
    }

    private static string? BuildFunctionRef(ProcedureResultColumn column)
    {
        if (column?.Reference == null)
        {
            return null;
        }

        if (!string.Equals(column.Reference.Kind, "Function", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return SnapshotWriterUtilities.ComposeSchemaObjectRef(column.Reference.Schema, column.Reference.Name);
    }
}

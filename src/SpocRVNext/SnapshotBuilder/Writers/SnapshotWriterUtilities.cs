using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Data.Models;
using SpocR.SpocRVNext.Data.Queries;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

internal static class SnapshotWriterUtilities
{
    internal static string NormalizeParameterName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return raw.TrimStart('@');
    }

    internal static string? BuildTypeRef(StoredProcedureInput input)
    {
        if (input == null)
        {
            return null;
        }

        if (input.IsTableType && !string.IsNullOrWhiteSpace(input.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(input.UserTypeName))
        {
            return BuildTypeRef(input.UserTypeSchemaName, input.UserTypeName);
        }

        if (!string.IsNullOrWhiteSpace(input.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(input.UserTypeName))
        {
            return BuildTypeRef(input.UserTypeSchemaName, input.UserTypeName);
        }

        if (!string.IsNullOrWhiteSpace(input.SqlTypeName))
        {
            var normalized = NormalizeSqlTypeName(input.SqlTypeName);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return BuildTypeRef("sys", normalized);
            }
        }

        return null;
    }

    internal static string? BuildTypeRef(Column column)
    {
        if (column == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(column.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(column.UserTypeName))
        {
            return BuildTypeRef(column.UserTypeSchemaName, column.UserTypeName);
        }

        if (!string.IsNullOrWhiteSpace(column.SqlTypeName))
        {
            var normalized = NormalizeSqlTypeName(column.SqlTypeName);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return BuildTypeRef("sys", normalized);
            }
        }

        return null;
    }

    internal static string? BuildTypeRef(ProcedureResultColumn column)
    {
        if (column == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(column.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(column.UserTypeName))
        {
            return BuildTypeRef(column.UserTypeSchemaName, column.UserTypeName);
        }

        var sqlType = column.SqlTypeName;
        if (string.IsNullOrWhiteSpace(sqlType) && !string.IsNullOrWhiteSpace(column.CastTargetType))
        {
            sqlType = column.CastTargetType;
        }

        if (!string.IsNullOrWhiteSpace(sqlType))
        {
            var normalized = NormalizeSqlTypeName(sqlType);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return BuildTypeRef("sys", normalized);
            }
        }

        return null;
    }

    internal static string? BuildTypeRef(FunctionParamRow parameter)
    {
        if (parameter == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(parameter.user_type_schema_name) && !string.IsNullOrWhiteSpace(parameter.user_type_name))
        {
            return BuildTypeRef(parameter.user_type_schema_name, parameter.user_type_name);
        }

        var sqlType = parameter.system_type_name ?? parameter.base_type_name;
        if (!string.IsNullOrWhiteSpace(sqlType))
        {
            var normalized = NormalizeSqlTypeName(sqlType);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return BuildTypeRef("sys", normalized);
            }
        }

        return null;
    }

    internal static string? BuildTypeRef(FunctionColumnRow column)
    {
        if (column == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(column.user_type_schema_name) && !string.IsNullOrWhiteSpace(column.user_type_name))
        {
            return BuildTypeRef(column.user_type_schema_name, column.user_type_name);
        }

        var sqlType = column.system_type_name ?? column.base_type_name;
        if (!string.IsNullOrWhiteSpace(sqlType))
        {
            var normalized = NormalizeSqlTypeName(sqlType);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return BuildTypeRef("sys", normalized);
            }
        }

        return null;
    }

    internal static string? BuildTypeRef(string? schema, string? name)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return string.Concat(schema.Trim(), ".", name.Trim());
    }

    internal static (string? Schema, string? Name) SplitTypeRef(string? typeRef)
    {
        if (string.IsNullOrWhiteSpace(typeRef))
        {
            return (null, null);
        }

        var parts = typeRef.Trim().Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            var schema = string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0];
            var name = string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1];
            return (schema, name);
        }

        var single = string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0];
        return (null, single);
    }

    internal static void RegisterTypeRef(ISet<string>? collector, string? typeRef)
    {
        if (collector == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(typeRef))
        {
            return;
        }

        var (schema, name) = SplitTypeRef(typeRef);
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        collector.Add(BuildKey(schema, name));
    }

    internal static string? NormalizeSqlTypeName(string? sqlTypeName)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeName))
        {
            return null;
        }

        return sqlTypeName.Trim().ToLowerInvariant();
    }

    internal static bool ShouldEmitIsNullable(bool value, string? typeRefOrTypeName)
        => ShouldEmitIsNullable(value ? (bool?)true : null, typeRefOrTypeName);

    internal static bool ShouldEmitIsNullable(bool? value, string? typeRefOrTypeName)
    {
        if (!value.HasValue || !value.Value)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(typeRefOrTypeName))
        {
            return true;
        }

        var (schema, _) = SplitTypeRef(typeRefOrTypeName);
        if (string.IsNullOrWhiteSpace(schema))
        {
            return true;
        }

        if (!string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    internal static bool ShouldEmitMaxLength(int value, string? typeRef)
    {
        if (value <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(typeRef))
        {
            return true;
        }

        var (schema, name) = SplitTypeRef(typeRef);
        if (!string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IsFixedSizeSysType(name);
    }

    internal static bool ShouldEmitMaxLength(int? value, string? typeRef)
    {
        if (!value.HasValue)
        {
            return false;
        }

        return ShouldEmitMaxLength(value.Value, typeRef);
    }

    internal static bool ShouldEmitPrecision(int? precision, string? typeRef)
    {
        if (!precision.HasValue || precision.Value <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(typeRef))
        {
            return true;
        }

        var (schema, name) = SplitTypeRef(typeRef);
        if (!string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name is "decimal" or "numeric" or "datetime2" or "datetimeoffset" or "time";
    }

    internal static bool ShouldEmitScale(int? scale, string? typeRef)
    {
        if (!scale.HasValue || scale.Value <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(typeRef))
        {
            return true;
        }

        var (schema, name) = SplitTypeRef(typeRef);
        if (!string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name is "decimal" or "numeric" or "datetime2" or "datetimeoffset" or "time";
    }

    private static bool IsFixedSizeSysType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name switch
        {
            "bigint" or
            "int" or
            "smallint" or
            "tinyint" or
            "bit" or
            "date" or
            "datetime" or
            "datetime2" or
            "datetimeoffset" or
            "smalldatetime" or
            "time" or
            "float" or
            "real" or
            "money" or
            "smallmoney" or
            "uniqueidentifier" or
            "rowversion" or
            "timestamp" or
            "sql_variant" or
            "hierarchyid" or
            "geometry" or
            "geography" or
            "xml" or
            "text" or
            "ntext" or
            "image" or
            "sysname" => true,
            _ => false
        };
    }

    internal static string BuildKey(string schema, string name)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return name ?? string.Empty;
        }

        return $"{schema}.{name}";
    }

    internal static string? ComposeSchemaObjectRef(string? schema, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var cleanName = name.Trim();
        if (cleanName.Length == 0)
        {
            return null;
        }

        var cleanSchema = string.IsNullOrWhiteSpace(schema) ? null : schema.Trim();
        return cleanSchema != null ? string.Concat(cleanSchema, ".", cleanName) : cleanName;
    }

    internal static string BuildArtifactFileName(string schema, string name)
    {
        var schemaSafe = NameSanitizer.SanitizeForFile(schema ?? string.Empty);
        var nameSafe = NameSanitizer.SanitizeForFile(name ?? string.Empty);
        if (string.IsNullOrWhiteSpace(schemaSafe))
        {
            return string.IsNullOrWhiteSpace(nameSafe) ? "artifact.json" : $"{nameSafe}.json";
        }

        if (string.IsNullOrWhiteSpace(nameSafe))
        {
            return $"{schemaSafe}.json";
        }

        return $"{schemaSafe}.{nameSafe}.json";
    }

    internal static string ComputeHash(string content)
        => ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty));

    internal static string ComputeHash(byte[] content)
        => ComputeHash(content.AsSpan());

    internal static string ComputeHash(ReadOnlySpan<byte> content)
    {
        var hashBytes = SHA256.HashData(content);
        return Convert.ToHexString(hashBytes).Substring(0, 16);
    }

    internal static async Task PersistSnapshotAsync(string filePath, byte[] content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tempPath = filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(filePath))
            {
                try
                {
                    File.Replace(tempPath, filePath, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(tempPath, filePath, overwrite: true);
                }
                catch (IOException)
                {
                    File.Copy(tempPath, filePath, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    internal static string? BuildSqlTypeName(FunctionParamRow parameter)
    {
        if (parameter == null)
        {
            return null;
        }

        return BuildSqlTypeNameCore(parameter.system_type_name ?? parameter.base_type_name, parameter.precision, parameter.scale, parameter.max_length, parameter.normalized_length);
    }

    internal static string? BuildSqlTypeName(FunctionColumnRow column)
    {
        if (column == null)
        {
            return null;
        }

        return BuildSqlTypeNameCore(column.system_type_name ?? column.base_type_name, column.precision, column.scale, column.max_length, column.normalized_length);
    }

    private static string? BuildSqlTypeNameCore(string? rawType, int precision, int scale, int maxLength, int normalizedLength)
    {
        var normalized = NormalizeSqlTypeName(rawType);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized is "decimal" or "numeric")
        {
            if (precision > 0)
            {
                return string.Concat(normalized, "(", precision.ToString(), ",", Math.Max(0, scale).ToString(), ")");
            }
        }

        if (normalized is "datetime2" or "datetimeoffset" or "time")
        {
            if (scale > 0)
            {
                return string.Concat(normalized, "(", Math.Max(0, scale).ToString(), ")");
            }
        }

        if (normalized is "binary" or "varbinary" or "char" or "nchar" or "varchar" or "nvarchar")
        {
            if (maxLength == -1)
            {
                return string.Concat(normalized, "(max)");
            }

            var length = normalizedLength > 0 ? normalizedLength : maxLength;
            if (length > 0)
            {
                return string.Concat(normalized, "(", length.ToString(), ")");
            }
        }

        return normalized;
    }
}

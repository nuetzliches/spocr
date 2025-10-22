using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SpocR.DataContext.Queries;

/// <summary>
/// Batch queries for scalar & table-valued functions (preview). Provides a single roundtrip returning
/// 3 result sets: functions, parameters, tvf columns.
/// </summary>
public static class FunctionQueries
{
    public static Task<List<FunctionRow>> FunctionListAsync(this DbContext context, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT s.name AS schema_name, o.name AS function_name, o.type AS type_code, o.modify_date, OBJECT_DEFINITION(o.object_id) AS definition, o.object_id AS object_id
FROM sys.objects o
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.type IN ('FN','IF','TF')
ORDER BY s.name, o.name;";
        return context.ListAsync<FunctionRow>(sql, new List<SqlParameter>(), cancellationToken);
    }
    public static Task<List<FunctionParamRow>> FunctionParametersAsync(this DbContext context, CancellationToken cancellationToken)
    {
    const string sql = @"SELECT p.object_id, p.parameter_id AS ordinal, p.name AS param_name,
 t.name AS system_type_name, p.max_length,
 CAST(p.is_output AS INT) AS is_output,
 IIF(t.name LIKE 'nvarchar%', p.max_length / 2, p.max_length) AS normalized_length,
 CAST(p.is_nullable AS INT) AS is_nullable,
 CASE WHEN p.has_default_value = 1 THEN 1 ELSE 0 END AS has_default_value,
 CAST(t.precision AS int) AS precision,
 CAST(t.scale AS int) AS scale,
 t_alias.name AS user_type_name,
 s_alias.name AS user_type_schema_name,
 t.name AS base_type_name
 FROM sys.parameters p
 INNER JOIN sys.types t ON t.user_type_id = p.system_type_id AND t.system_type_id = p.system_type_id
 LEFT JOIN sys.types t_alias ON t_alias.system_type_id = p.system_type_id AND t_alias.user_type_id = p.user_type_id AND t_alias.is_user_defined = 1 AND t_alias.is_table_type = 0
 LEFT JOIN sys.schemas s_alias ON s_alias.schema_id = t_alias.schema_id
 WHERE p.object_id IN (SELECT object_id FROM sys.objects WHERE type IN ('FN','IF','TF'))
 ORDER BY p.object_id, p.parameter_id;";
        return context.ListAsync<FunctionParamRow>(sql, new List<SqlParameter>(), cancellationToken);
    }
    public static Task<List<FunctionColumnRow>> FunctionTvfColumnsAsync(this DbContext context, CancellationToken cancellationToken)
    {
    const string sql = @"SELECT c.object_id, c.column_id AS ordinal, c.name AS column_name,
 t.name AS system_type_name,
 CAST(c.is_nullable AS INT) AS is_nullable,
 c.max_length, IIF(t.name LIKE 'nvarchar%', c.max_length / 2, c.max_length) AS normalized_length,
 CAST(t.precision AS int) AS precision,
 CAST(t.scale AS int) AS scale,
 t_alias.name AS user_type_name,
 s_alias.name AS user_type_schema_name,
 t.name AS base_type_name
 FROM sys.columns c
 INNER JOIN sys.types t ON t.user_type_id = c.system_type_id AND t.system_type_id = c.system_type_id
 LEFT JOIN sys.types t_alias ON t_alias.system_type_id = c.system_type_id AND t_alias.user_type_id = c.user_type_id AND t_alias.is_user_defined = 1 AND t_alias.is_table_type = 0
 LEFT JOIN sys.schemas s_alias ON s_alias.schema_id = t_alias.schema_id
 WHERE c.object_id IN (SELECT object_id FROM sys.objects WHERE type IN ('IF','TF'))
 ORDER BY c.object_id, c.column_id;";
        return context.ListAsync<FunctionColumnRow>(sql, new List<SqlParameter>(), cancellationToken);
    }

    public static Task<List<FunctionDependencyRow>> FunctionDependenciesAsync(this DbContext context, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT d.referencing_id, d.referenced_id
FROM sys.sql_expression_dependencies d
INNER JOIN sys.objects o_ref ON o_ref.object_id = d.referenced_id AND o_ref.type IN ('FN','IF','TF')
INNER JOIN sys.objects o_src ON o_src.object_id = d.referencing_id AND o_src.type IN ('FN','IF','TF')
WHERE d.referencing_id IN (SELECT object_id FROM sys.objects WHERE type IN ('FN','IF','TF'))
ORDER BY d.referencing_id, d.referenced_id;";
        return context.ListAsync<FunctionDependencyRow>(sql, new List<SqlParameter>(), cancellationToken);
    }
}

public class FunctionRow { public string schema_name { get; set; } public string function_name { get; set; } public string type_code { get; set; } public System.DateTime modify_date { get; set; } public string definition { get; set; } public int object_id { get; set; } }
public class FunctionParamRow {
    public int object_id { get; set; }
    public int ordinal { get; set; }
    public string param_name { get; set; }
    public string system_type_name { get; set; }
    public int max_length { get; set; }
    public int normalized_length { get; set; }
    public int is_output { get; set; }
    public int is_nullable { get; set; }
    public int has_default_value { get; set; }
    public int precision { get; set; }
    public int scale { get; set; }
    public string user_type_name { get; set; }
    public string user_type_schema_name { get; set; }
    public string base_type_name { get; set; }
}
public class FunctionColumnRow {
    public int object_id { get; set; }
    public int ordinal { get; set; }
    public string column_name { get; set; }
    public string system_type_name { get; set; }
    public int is_nullable { get; set; }
    public int max_length { get; set; }
    public int normalized_length { get; set; }
    public int precision { get; set; }
    public int scale { get; set; }
    public string user_type_name { get; set; }
    public string user_type_schema_name { get; set; }
    public string base_type_name { get; set; }
}
public class FunctionDependencyRow { public int referencing_id { get; set; } public int referenced_id { get; set; } }

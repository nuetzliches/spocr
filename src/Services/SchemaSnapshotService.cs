using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.Models;

namespace SpocR.Services;

public interface ISchemaSnapshotService
{
    SchemaSnapshot Load(string fingerprint);
    void Save(SchemaSnapshot snapshot);
    string BuildFingerprint(string serverName, string databaseName, IEnumerable<string> includedSchemas, int procedureCount, int udttCount, int parserVersion);
}

public class SchemaSnapshotService : ISchemaSnapshotService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string EnsureDir()
    {
        var working = Utils.DirectoryUtils.GetWorkingDirectory();
        if (string.IsNullOrEmpty(working)) return null;
        var dir = Path.Combine(working, ".spocr", "schema");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    public string BuildFingerprint(string serverName, string databaseName, IEnumerable<string> includedSchemas, int procedureCount, int udttCount, int parserVersion)
    {
        var parts = new[]
        {
            serverName?.Trim().ToLowerInvariant() ?? "?",
            databaseName?.Trim().ToLowerInvariant() ?? "?",
            string.Join(';', (includedSchemas ?? Array.Empty<string>()).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)),
            procedureCount.ToString(),
            udttCount.ToString(),
            parserVersion.ToString()
        };
        var raw = string.Join('|', parts);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return hash.Substring(0, 16);
    }

    public SchemaSnapshot Load(string fingerprint)
    {
        try
        {
            var dir = EnsureDir();
            if (dir == null) return null;
            var path = Path.Combine(dir, fingerprint + ".json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SchemaSnapshot>(json, _jsonOptions);
        }
        catch { return null; }
    }

    public void Save(SchemaSnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrEmpty(snapshot.Fingerprint)) return;
        try
        {
            var dir = EnsureDir();
            if (dir == null) return;
            var path = Path.Combine(dir, snapshot.Fingerprint + ".json");

            SnapshotResultColumn ProcessColumn(SnapshotResultColumn c)
            {
                var clone = new SnapshotResultColumn
                {
                    Name = c.Name,
                    SqlTypeName = c.SqlTypeName,
                    IsNullable = c.IsNullable == false ? null : c.IsNullable,
                    MaxLength = c.MaxLength,
                    UserTypeSchemaName = c.UserTypeSchemaName,
                    UserTypeName = c.UserTypeName,
                    IsNestedJson = c.IsNestedJson == true ? true : null,
                    ReturnsJson = c.ReturnsJson == true ? true : null,
                    ReturnsJsonArray = c.ReturnsJsonArray == true ? true : null,
                    JsonRootProperty = string.IsNullOrWhiteSpace(c.JsonRootProperty) ? null : c.JsonRootProperty,
                };
                if (c.Columns != null && c.Columns.Count > 0)
                {
                    var nested = c.Columns.Select(ProcessColumn).Where(n => n != null).ToList();
                    clone.Columns = nested.Count > 0 ? nested : null; // nested JSON columns (column-level)
                }
                else clone.Columns = null; // omit empty
                // Legacy fields removed (JsonPath/JsonResult) by not setting
                return clone;
            }

            var prunedSnapshot = new SchemaSnapshot
            {
                SchemaVersion = snapshot.SchemaVersion,
                Fingerprint = snapshot.Fingerprint,
                Database = snapshot.Database,
                Schemas = snapshot.Schemas,
                UserDefinedTableTypes = snapshot.UserDefinedTableTypes,
                Parser = snapshot.Parser,
                Stats = snapshot.Stats,
                Procedures = snapshot.Procedures?.Select(p => new SnapshotProcedure
                {
                    Schema = p.Schema,
                    Name = p.Name,
                    Sql = p.Sql,
                    Inputs = p.Inputs,
                    ResultSets = p.ResultSets?.Select(rs => new SnapshotResultSet
                    {
                        ReturnsJson = rs.ReturnsJson,
                        ReturnsJsonArray = rs.ReturnsJsonArray,
                        JsonRootProperty = string.IsNullOrWhiteSpace(rs.JsonRootProperty) ? null : rs.JsonRootProperty,
                        ExecSourceSchemaName = rs.ExecSourceSchemaName,
                        ExecSourceProcedureName = rs.ExecSourceProcedureName,
                        HasSelectStar = rs.HasSelectStar == true ? true : null,
                        Columns = rs.Columns?.Select(ProcessColumn).Where(c => c != null).ToList() ?? new List<SnapshotResultColumn>()
                    }).Where(r => r != null).ToList() ?? new List<SnapshotResultSet>()
                }).ToList() ?? new List<SnapshotProcedure>()
            };

            // Prune empty collections where appropriate
            foreach (var proc in prunedSnapshot.Procedures)
            {
                if (proc.ResultSets != null)
                {
                    foreach (var rs in proc.ResultSets)
                    {
                        if (rs.Columns != null && rs.Columns.Count == 0) rs.Columns = null; // omit empty top-level columns
                        if (rs.Columns != null)
                        {
                            foreach (var c in rs.Columns)
                            {
                                PruneNested(c);
                            }
                        }
                        if (rs.HasSelectStar == false) rs.HasSelectStar = null; // prune false
                    }
                }
            }

            // Serialize
            var json = JsonSerializer.Serialize(prunedSnapshot, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch { /* swallow snapshot write errors */ }
    }

    private static void PruneNested(SnapshotResultColumn column)
    {
        if (column.Columns != null && column.Columns.Count == 0) column.Columns = null;
        if (column.Columns == null) return;
        foreach (var child in column.Columns)
        {
            PruneNested(child);
        }
        // After recursion, if all children were pruned and list became empty -> null
        if (column.Columns != null && column.Columns.Count == 0) column.Columns = null;
    }
}

public class SchemaSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string Fingerprint { get; set; }
    [JsonIgnore] // Excluded from persisted snapshot to avoid nondeterministic Git diffs
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public SnapshotDatabase Database { get; set; }
    public List<SnapshotProcedure> Procedures { get; set; } = new();
    public List<SnapshotSchema> Schemas { get; set; } = new();
    public List<SnapshotUdtt> UserDefinedTableTypes { get; set; } = new();
    // Preview: Functions (scalar + TVF) captured independently of schema allow-list.
    public int? FunctionsVersion { get; set; } // set to 1 when functions populated
    public List<SnapshotFunction> Functions { get; set; } = new();
    public SnapshotParserInfo Parser { get; set; }
    public SnapshotStats Stats { get; set; }
}

public class SnapshotDatabase
{
    public string ServerHash { get; set; }
    public string Name { get; set; }
}

public class SnapshotProcedure
{
    public string Schema { get; set; }
    public string Name { get; set; }
    public string Sql { get; set; } // Raw procedure definition (body) for downstream naming heuristics
    public List<SnapshotInput> Inputs { get; set; } = new();
    public List<SnapshotResultSet> ResultSets { get; set; } = new();
}

public class SnapshotInput
{
    public string Name { get; set; }
    public string TableTypeSchema { get; set; }
    public string TableTypeName { get; set; }
    public bool? IsOutput { get; set; }
    public string SqlTypeName { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public bool? HasDefaultValue { get; set; } // nur schreiben wenn true
}

public class SnapshotResultSet
{
    public bool ReturnsJson { get; set; }
    public bool ReturnsJsonArray { get; set; }
    public string JsonRootProperty { get; set; }
    public List<SnapshotResultColumn> Columns { get; set; } = new();
    public string ExecSourceSchemaName { get; set; }
    public string ExecSourceProcedureName { get; set; }
    public bool? HasSelectStar { get; set; } // nullable to allow pruning when false
}

public class SnapshotResultColumn
{
    public string Name { get; set; }
    public string SqlTypeName { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public string UserTypeSchemaName { get; set; }
    public string UserTypeName { get; set; }
    // Flattened nested JSON structure (v6): when IsNestedJson=true these flags describe the nested JSON under this column
    public bool? IsNestedJson { get; set; }
    public bool? ReturnsJson { get; set; }
    public bool? ReturnsJsonArray { get; set; }
    public string JsonRootProperty { get; set; }
    public List<SnapshotResultColumn> Columns { get; set; } = new(); // renamed from JsonColumns in v7
    // Legacy v5 fields kept for backward compatibility during load; will be pruned on save.
    [Obsolete] public string JsonPath { get; set; }
    [Obsolete] public SnapshotNestedJson JsonResult { get; set; }
}

public class SnapshotNestedJson
{
    public bool ReturnsJson { get; set; }
    public bool ReturnsJsonArray { get; set; }
    public string JsonRootProperty { get; set; }
    public List<SnapshotResultColumn> Columns { get; set; } = new();
}

public class SnapshotSchema
{
    public string Name { get; set; }
    public List<string> TableTypeRefs { get; set; } = new(); // schema.name
}

public class SnapshotUdtt
{
    public string Schema { get; set; }
    public string Name { get; set; }
    public int? UserTypeId { get; set; }
    public List<SnapshotUdttColumn> Columns { get; set; } = new();
    public string Hash { get; set; }
}

public class SnapshotUdttColumn
{
    public string Name { get; set; }
    public string SqlTypeName { get; set; }
    public bool IsNullable { get; set; }
    public int MaxLength { get; set; }
}

public class SnapshotFunction
{
    public string Schema { get; set; }
    public string Name { get; set; }
    public bool? IsTableValued { get; set; } // false wird vor Persistierung gepruned (nur true schreiben)
    public string ReturnSqlType { get; set; } // leer für TVF oder gepruned bei JSON
    public int? ReturnMaxLength { get; set; } // nur für skalare Funktionen
    public bool? ReturnIsNullable { get; set; } // nur für skalare Funktionen (bei JSON gepruned)
    public List<SnapshotFunctionParameter> Parameters { get; set; } = new();
    public List<SnapshotFunctionColumn> Columns { get; set; } = new(); // Für TVF oder JSON Spalten (Nested möglich)
    public bool? ReturnsJson { get; set; } // heuristisch aus Definition abgeleitet (FOR JSON)
    public bool? ReturnsJsonArray { get; set; } // true wenn FOR JSON ohne WITHOUT_ARRAY_WRAPPER
    public string JsonRootProperty { get; set; } // optional aus Pfad ableitbar (zukünftige AST-Analyse)
    public bool? IsEncrypted { get; set; } // nur schreiben wenn true
    public List<string> Dependencies { get; set; } = new(); // Liste anderer Funktionen (schema.name) von denen diese Funktion direkt abhängt
}

public class SnapshotFunctionParameter
{
    public string Name { get; set; }
    public string TableTypeSchema { get; set; }
    public string TableTypeName { get; set; }
    public bool? IsOutput { get; set; }
    public string SqlTypeName { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    // Hinweis: Default-Wert Information wird vorerst nicht persistiert zur Vereinheitlichung mit StoredProcedure Inputs.
    public bool? HasDefaultValue { get; set; } // nur true persistieren
}

public class SnapshotFunctionColumn
{
    public string Name { get; set; }
    public string SqlTypeName { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    // Nested JSON Unterstützung (analog ResultSet Columns, aber leichtgewichtig)
    public bool? IsNestedJson { get; set; } // true wenn Unterstruktur (Objekt/Array) enthalten ist
    public bool? ReturnsJson { get; set; } // Kennzeichnet JSON Subselect
    public bool? ReturnsJsonArray { get; set; } // true wenn Subselect ein Array zurück gibt
    public string JsonRootProperty { get; set; } // Root('x') oder impliziter Alias
    public List<SnapshotFunctionColumn> Columns { get; set; } = new(); // rekursive Verschachtelung
}

public class SnapshotParserInfo
{
    public string ToolVersion { get; set; }
    public int ResultSetParserVersion { get; set; }
}

public class SnapshotStats
{
    public int ProcedureTotal { get; set; }
    public int ProcedureSkipped { get; set; }
    public int ProcedureLoaded { get; set; }
    public int UdttTotal { get; set; }
}

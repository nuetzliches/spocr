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
            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch { }
    }
}

public class SchemaSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string Fingerprint { get; set; }
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public SnapshotDatabase Database { get; set; }
    public List<SnapshotProcedure> Procedures { get; set; } = new();
    public List<SnapshotSchema> Schemas { get; set; } = new();
    public List<SnapshotUdtt> UserDefinedTableTypes { get; set; } = new();
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
    public List<SnapshotInput> Inputs { get; set; } = new();
    public List<SnapshotResultSet> ResultSets { get; set; } = new();
}

public class SnapshotInput
{
    public string Name { get; set; }
    public bool IsTableType { get; set; }
    public string TableTypeSchema { get; set; }
    public string TableTypeName { get; set; }
    public bool IsOutput { get; set; }
    public string SqlTypeName { get; set; }
    public bool IsNullable { get; set; }
    public int MaxLength { get; set; }
}

public class SnapshotResultSet
{
    public bool ReturnsJson { get; set; }
    public bool ReturnsJsonArray { get; set; }
    public bool ReturnsJsonWithoutArrayWrapper { get; set; }
    public string JsonRootProperty { get; set; }
    public List<SnapshotResultColumn> Columns { get; set; } = new();
    public string ExecSourceSchemaName { get; set; }
    public string ExecSourceProcedureName { get; set; }
    public bool HasSelectStar { get; set; }
}

public class SnapshotResultColumn
{
    public string Name { get; set; }
    public string SqlTypeName { get; set; }
    public bool IsNullable { get; set; }
    public int MaxLength { get; set; }
    public string UserTypeSchemaName { get; set; }
    public string UserTypeName { get; set; }
    public string JsonPath { get; set; }
    public SnapshotNestedJson JsonResult { get; set; }
}

public class SnapshotNestedJson
{
    public bool ReturnsJson { get; set; }
    public bool ReturnsJsonArray { get; set; }
    public bool ReturnsJsonWithoutArrayWrapper { get; set; }
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

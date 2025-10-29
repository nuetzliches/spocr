using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.Models;
using SpocR.SpocRVNext.SnapshotBuilder.Writers;

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
        var dir = Path.Combine(working, ".spocr", "cache");
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
            var cacheDir = EnsureDir();
            if (cacheDir == null) return null;

            var candidateDirs = new List<string>();
            if (!string.IsNullOrEmpty(cacheDir))
            {
                candidateDirs.Add(cacheDir);
            }

            var legacyCacheDir = ResolveLegacyCacheDir();
            if (!string.IsNullOrEmpty(legacyCacheDir))
            {
                candidateDirs.Add(legacyCacheDir);
            }

            var legacySchemaDir = ResolveLegacySchemaDir();
            if (!string.IsNullOrEmpty(legacySchemaDir))
            {
                candidateDirs.Add(legacySchemaDir);
            }

            string pathToLoad = null;
            foreach (var dir in candidateDirs)
            {
                if (string.IsNullOrEmpty(dir))
                {
                    continue;
                }

                var candidatePath = Path.Combine(dir, fingerprint + ".json");
                if (File.Exists(candidatePath))
                {
                    pathToLoad = candidatePath;
                    break;
                }
            }

            if (string.IsNullOrEmpty(pathToLoad) || !File.Exists(pathToLoad)) return null;
            var json = File.ReadAllText(pathToLoad);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            SchemaCacheDocument cacheDocument = null;
            try
            {
                cacheDocument = JsonSerializer.Deserialize<SchemaCacheDocument>(json, _jsonOptions);
            }
            catch (JsonException)
            {
                cacheDocument = null;
            }

            if (cacheDocument != null)
            {
                if (cacheDocument.CacheVersion < SchemaCacheDocument.CurrentVersion && RequiresLegacyConversion(cacheDocument, json))
                {
                    try
                    {
                        var legacy = JsonSerializer.Deserialize<LegacySchemaCacheDocument>(json, _jsonOptions);
                        cacheDocument = ConvertLegacyCacheDocument(legacy) ?? cacheDocument;
                    }
                    catch (JsonException)
                    {
                        // ignore legacy conversion failures; fall back to existing document
                    }
                }

                return ConvertFromCacheDocument(cacheDocument);
            }

            try
            {
                return JsonSerializer.Deserialize<SchemaSnapshot>(json, _jsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
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

            var schemaNames = (snapshot.Schemas ?? new List<SnapshotSchema>())
                .Select(s => s?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var procedures = (snapshot.Procedures ?? new List<SnapshotProcedure>())
                .Where(p => !string.IsNullOrWhiteSpace(p?.Schema) && !string.IsNullOrWhiteSpace(p?.Name))
                .Select(p => new SchemaCacheProcedure
                {
                    Schema = p.Schema,
                    Name = p.Name
                })
                .OrderBy(p => p.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tables = (snapshot.Tables ?? new List<SnapshotTable>())
                .Where(t => !string.IsNullOrWhiteSpace(t?.Schema) && !string.IsNullOrWhiteSpace(t?.Name))
                .Select(t => new SchemaCacheTable
                {
                    Schema = t.Schema,
                    Name = t.Name,
                    ColumnCount = t.Columns?.Count ?? 0,
                    ColumnsHash = ComputeTableColumnsHash(t.Columns)
                })
                .OrderBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var document = new SchemaCacheDocument
            {
                CacheVersion = SchemaCacheDocument.CurrentVersion,
                SchemaVersion = snapshot.SchemaVersion,
                Fingerprint = snapshot.Fingerprint,
                Database = snapshot.Database != null
                    ? new SchemaCacheDatabase
                    {
                        ServerHash = snapshot.Database.ServerHash,
                        Name = snapshot.Database.Name
                    }
                    : null,
                Schemas = schemaNames.Select(n => new SchemaCacheSchema { Name = n }).ToList(),
                Procedures = procedures,
                Tables = tables
            };

            var json = JsonSerializer.Serialize(document, _jsonOptions);
            File.WriteAllText(path, json);

            TryDeleteLegacyCacheSnapshot(snapshot.Fingerprint);

            PruneLegacySchemaSnapshots();
        }
        catch { /* swallow snapshot write errors */ }
    }

    // Legacy layout: deterministic artefacts continue to live in .spocr/schema for git tracking.
    // We still probe that directory when upgrading older snapshots or bridging consumers that expect the historical layout.
    private static string ResolveLegacySchemaDir()
    {
        var working = Utils.DirectoryUtils.GetWorkingDirectory();
        if (string.IsNullOrEmpty(working)) return string.Empty;
        return Path.Combine(working, ".spocr", "schema");
    }

    private static string ResolveLegacyCacheDir()
    {
        var working = Utils.DirectoryUtils.GetWorkingDirectory();
        if (string.IsNullOrEmpty(working)) return string.Empty;
        return Path.Combine(working, ".spocr", "cache", "schema");
    }

    private static void TryDeleteLegacyCacheSnapshot(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        try
        {
            var legacyDir = ResolveLegacyCacheDir();
            if (string.IsNullOrEmpty(legacyDir) || !Directory.Exists(legacyDir))
            {
                return;
            }

            var legacyPath = Path.Combine(legacyDir, fingerprint + ".json");
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
        catch
        {
            // best effort cleanup only
        }
    }

    private static void PruneLegacySchemaSnapshots()
    {
        try
        {
            var legacyDir = ResolveLegacySchemaDir();
            if (string.IsNullOrEmpty(legacyDir) || !Directory.Exists(legacyDir))
            {
                return;
            }

            var files = Directory.GetFiles(legacyDir, "*.json", SearchOption.TopDirectoryOnly)
                .Where(f => !string.Equals(Path.GetFileName(f), "index.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in files)
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // best-effort cleanup; ignore failures
        }
    }

    private static bool RequiresLegacyConversion(SchemaCacheDocument document, string json)
    {
        if (document == null)
        {
            return false;
        }

        if (document.Procedures == null || document.Procedures.Count == 0)
        {
            return ContainsInputsMarker(json);
        }

        var allHaveParameters = document.Procedures.All(p => p != null && p.Parameters != null && p.Parameters.Count > 0);
        if (allHaveParameters)
        {
            return false;
        }

        return ContainsInputsMarker(json);
    }

    private static bool ContainsInputsMarker(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        return json.IndexOf("\"Inputs\"", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static SchemaCacheDocument ConvertLegacyCacheDocument(LegacySchemaCacheDocument legacy)
    {
        if (legacy == null)
        {
            return null;
        }

        var convertedProcedures = (legacy.Procedures ?? new List<LegacySchemaCacheProcedure>())
            .Where(p => !string.IsNullOrWhiteSpace(p?.Schema) && !string.IsNullOrWhiteSpace(p?.Name))
            .Select(p => new SchemaCacheProcedure
            {
                Schema = p.Schema,
                Name = p.Name,
                Parameters = CloneParameters(p.Inputs)
            })
            .ToList();

        return new SchemaCacheDocument
        {
            CacheVersion = SchemaCacheDocument.CurrentVersion,
            SchemaVersion = legacy.SchemaVersion,
            Fingerprint = legacy.Fingerprint ?? string.Empty,
            Database = legacy.Database,
            Schemas = legacy.Schemas ?? new List<SchemaCacheSchema>(),
            Procedures = convertedProcedures,
            Tables = new List<SchemaCacheTable>()
        };
    }

    private static SchemaSnapshot ConvertFromCacheDocument(SchemaCacheDocument document)
    {
        if (document == null)
        {
            return null;
        }

        var schemaNames = (document.Schemas ?? new List<SchemaCacheSchema>())
            .Select(s => s?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var snapshot = new SchemaSnapshot
        {
            SchemaVersion = document.SchemaVersion,
            Fingerprint = document.Fingerprint ?? string.Empty,
            Database = document.Database != null
                ? new SnapshotDatabase
                {
                    ServerHash = document.Database.ServerHash,
                    Name = document.Database.Name
                }
                : null,
            Schemas = schemaNames.Select(n => new SnapshotSchema
            {
                Name = n,
                TableTypeRefs = new List<string>()
            }).ToList(),
            Procedures = (document.Procedures ?? new List<SchemaCacheProcedure>())
                .Where(p => !string.IsNullOrWhiteSpace(p?.Schema) && !string.IsNullOrWhiteSpace(p?.Name))
                .Select(p => new SnapshotProcedure
                {
                    Schema = p.Schema,
                    Name = p.Name,
                    Inputs = CloneParameters(p.Parameters),
                    ResultSets = new List<SnapshotResultSet>()
                })
                .ToList(),
            UserDefinedTableTypes = new List<SnapshotUdtt>(),
            Tables = (document.Tables ?? new List<SchemaCacheTable>())
                .Where(t => !string.IsNullOrWhiteSpace(t?.Schema) && !string.IsNullOrWhiteSpace(t?.Name))
                .Select(t => new SnapshotTable
                {
                    Schema = t.Schema,
                    Name = t.Name,
                    ColumnCount = t.ColumnCount,
                    ColumnsHash = t.ColumnsHash,
                    Columns = new List<SnapshotTableColumn>()
                })
                .ToList(),
            Views = new List<SnapshotView>(),
            UserDefinedTypes = new List<SnapshotUserDefinedType>(),
            Parser = null,
            Stats = null
        };

        return snapshot;
    }

    private static List<SnapshotInput> CloneParameters(IEnumerable<SnapshotInput>? inputs)
    {
        var result = new List<SnapshotInput>();
        if (inputs == null)
        {
            return result;
        }

        foreach (var input in inputs)
        {
            var clone = CloneParameter(input);
            if (clone != null)
            {
                result.Add(clone);
            }
        }

        return result;
    }

    private static SnapshotInput CloneParameter(SnapshotInput source)
    {
        if (source == null)
        {
            return null;
        }

        return new SnapshotInput
        {
            Name = source.Name ?? string.Empty,
            TypeRef = NormalizeOrNull(source.TypeRef),
            TableTypeSchema = NormalizeOrNull(source.TableTypeSchema),
            TableTypeName = NormalizeOrNull(source.TableTypeName),
            IsOutput = source.IsOutput == true ? true : null,
            IsNullable = source.IsNullable == true ? true : null,
            MaxLength = source.MaxLength.HasValue && source.MaxLength.Value > 0 ? source.MaxLength : null,
            HasDefaultValue = source.HasDefaultValue == true ? true : null,
            TypeSchema = NormalizeOrNull(source.TypeSchema),
            TypeName = NormalizeOrNull(source.TypeName),
            Precision = source.Precision.HasValue && source.Precision.Value > 0 ? source.Precision : null,
            Scale = source.Scale.HasValue && source.Scale.Value > 0 ? source.Scale : null
        };
    }

    private static string NormalizeOrNull(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string ComputeTableColumnsHash(IReadOnlyList<SnapshotTableColumn> columns)
    {
        if (columns == null || columns.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(columns.Count);
        foreach (var column in columns)
        {
            if (column == null || string.IsNullOrWhiteSpace(column.Name))
            {
                continue;
            }

            var part = string.Join("|", new[]
            {
                column.Name.Trim(),
                NormalizeTypeRef(column.TypeRef),
                column.IsNullable == true ? "1" : "0",
                FormatNumeric(column.MaxLength),
                FormatNumeric(column.Precision),
                FormatNumeric(column.Scale),
                column.IsIdentity == true ? "1" : "0"
            });

            parts.Add(part);
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        var payload = string.Join(";", parts);
        return string.IsNullOrEmpty(payload)
            ? string.Empty
            : SnapshotWriterUtilities.ComputeHash(payload);
    }

    private static string NormalizeTypeRef(string? typeRef)
    {
        if (string.IsNullOrWhiteSpace(typeRef))
        {
            return string.Empty;
        }

        return typeRef.Trim().ToLowerInvariant();
    }

    private static string FormatNumeric(int? value)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

        if (value.Value <= 0)
        {
            return string.Empty;
        }

        return value.Value.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class SchemaCacheDocument
    {
        public const int CurrentVersion = 3;
        public int CacheVersion { get; set; } = CurrentVersion;
        public int SchemaVersion { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public SchemaCacheDatabase Database { get; set; }
        public List<SchemaCacheSchema> Schemas { get; set; } = new();
        public List<SchemaCacheProcedure> Procedures { get; set; } = new();
        public List<SchemaCacheTable> Tables { get; set; } = new();
    }

    private sealed class SchemaCacheSchema
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class SchemaCacheProcedure
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<SnapshotInput>? Parameters { get; set; }

        [JsonPropertyName("Inputs")]
        public List<SnapshotInput>? LegacyInputs
        {
            set
            {
                if (value == null || value.Count == 0)
                {
                    return;
                }

                Parameters = value;
            }
        }
    }

    private sealed class SchemaCacheTable
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int ColumnCount { get; set; }
        public string ColumnsHash { get; set; } = string.Empty;
    }

    private sealed class SchemaCacheDatabase
    {
        public string ServerHash { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class LegacySchemaCacheDocument
    {
        public int CacheVersion { get; set; }
        public int SchemaVersion { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public SchemaCacheDatabase Database { get; set; }
        public List<SchemaCacheSchema> Schemas { get; set; } = new();
        public List<LegacySchemaCacheProcedure> Procedures { get; set; } = new();
    }

    private sealed class LegacySchemaCacheProcedure
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<SnapshotInput> Inputs { get; set; } = new();
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
    // Neuer leichtgewichtiger Basis-Snapshot für Tabellen (nur Schema, Name, Columns) – dient AST Typauflösung
    public List<SnapshotTable> Tables { get; set; } = new();
    // Views analog Tabellen (aktuell ohne Dependencies; Erweiterung v5 geplant)
    public List<SnapshotView> Views { get; set; } = new();
    // User Defined Scalar Types (keine Table Types) – notwendig vor Tabellen/Views zum Auflösen von Alias-Typen
    // Umbenennung: 'UserDefinedTypes' für Klarheit gegenüber TableTypes
    public List<SnapshotUserDefinedType> UserDefinedTypes { get; set; } = new();
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
    public List<SnapshotInput> Inputs { get; set; } = new();
    public List<SnapshotResultSet> ResultSets { get; set; } = new();
}

public class SnapshotInput
{
    public string Name { get; set; }
    public string? TypeRef { get; set; }
    public string? TableTypeSchema { get; set; }
    public string? TableTypeName { get; set; }
    public bool? IsOutput { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public bool? HasDefaultValue { get; set; } // nur schreiben wenn true
    public string? TypeSchema { get; set; }
    public string? TypeName { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
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
    public SnapshotColumnReference Reference { get; set; }
}

public class SnapshotResultColumn
{
    public string Name { get; set; }
    public string? TypeRef { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    // Präzision & Scale für decimal/numeric (oder time/datetime2 falls benötigt). 0/Null wird gepruned.
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    // Identity-Marker (nur true persistieren). Für Tabellen/Views/SP Outputs relevant, bei Prozedur-ResultSets optional falls aus DMV erkannt.
    public bool? IsIdentity { get; set; }
    // Flattened nested JSON structure (v6): when IsNestedJson=true these flags describe the nested JSON under this column
    public bool? IsNestedJson { get; set; }
    public bool? ReturnsJson { get; set; }
    public bool? ReturnsJsonArray { get; set; }
    public string JsonRootProperty { get; set; }
    public List<SnapshotResultColumn> Columns { get; set; } = new(); // renamed from JsonColumns in v7
    // Deferred Funktions-Expansion: Persistiere Referenz & Flag
    public SnapshotColumnReference Reference { get; set; }
    public bool? DeferredJsonExpansion { get; set; }
}

public sealed class SnapshotColumnReference
{
    public string Kind { get; set; } // Function | View | Procedure
    public string Schema { get; set; }
    public string Name { get; set; }
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
    public string? TypeRef { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
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
    public string? TypeRef { get; set; }
    public bool? IsOutput { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    // Hinweis: Default-Wert Information wird vorerst nicht persistiert zur Vereinheitlichung mit StoredProcedure Inputs.
    public bool? HasDefaultValue { get; set; } // nur true persistieren
}

public class SnapshotFunctionColumn
{
    public string Name { get; set; }
    public string? TypeRef { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool? IsIdentity { get; set; }
    // Nested JSON Unterstützung (analog ResultSet Columns, aber leichtgewichtig)
    public bool? IsNestedJson { get; set; } // true wenn Unterstruktur (Objekt/Array) enthalten ist
    public bool? ReturnsJson { get; set; } // Kennzeichnet JSON Subselect
    public bool? ReturnsJsonArray { get; set; } // true wenn Subselect ein Array zurück gibt
    public string JsonRootProperty { get; set; } // Root('x') oder impliziter Alias
    public List<SnapshotFunctionColumn> Columns { get; set; } = new(); // rekursive Verschachtelung
}

// --- Neue Basis-Snapshot Modelle (Prio 1) ---
public class SnapshotTable
{
    public string Schema { get; set; }
    public string Name { get; set; }
    [JsonIgnore]
    public int? ColumnCount { get; set; }

    [JsonIgnore]
    public string? ColumnsHash { get; set; }

    public List<SnapshotTableColumn> Columns { get; set; } = new();
}

public class SnapshotTableColumn
{
    public string Name { get; set; }
    public string? TypeRef { get; set; }
    public bool? IsNullable { get; set; } // false wird gepruned bei Persistierung (Analog zu anderen Modellen – Implementierung folgt im Writer)
    public int? MaxLength { get; set; } // null wenn 0 oder nicht zutreffend
    public bool? IsIdentity { get; set; } // nur true persistieren
    public int? Precision { get; set; }
    public int? Scale { get; set; }
}

public class SnapshotView
{
    public string Schema { get; set; }
    public string Name { get; set; }
    public List<SnapshotViewColumn> Columns { get; set; } = new();
}

public class SnapshotViewColumn
{
    public string Name { get; set; }
    public string? TypeRef { get; set; }
    public bool? IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
}

public class SnapshotUserDefinedType
{
    public string Schema { get; set; } // sys / dbo / benutzerdefiniert
    public string Name { get; set; }
    public string BaseSqlTypeName { get; set; } // z.B. nvarchar, int, decimal
    public int? MaxLength { get; set; } // für (n)varchar, varbinary
    public int? Precision { get; set; } // für decimal/num
    public int? Scale { get; set; } // für decimal/num
    public bool? IsNullable { get; set; } // falls ermittelbar (scalar UDTs oft nicht nullable direkt)
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
    // Erweiterung (Prio 1): Basis-Zählwerte für neue Snapshot Artefakte
    public int TableTotal { get; set; }
    public int ViewTotal { get; set; }
    public int UserDefinedTypeTotal { get; set; }
}

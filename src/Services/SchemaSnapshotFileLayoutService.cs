using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.Models;

namespace SpocR.Services;

/// <summary>
/// New file-based snapshot writer: creates an index.json plus subfolders 'procedures/' and 'tabletypes/'.
/// Migration approach: legacy monolithic snapshot file remains for fallback during the transition; this service can run in parallel.
/// No fingerprint in the folder name – index.json stores the global fingerprint and hashes of individual files.
/// </summary>
public sealed class SchemaSnapshotFileLayoutService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string EnsureBaseDir()
    {
        var working = Utils.DirectoryUtils.GetWorkingDirectory();
        if (string.IsNullOrEmpty(working)) return null;
        var baseDir = Path.Combine(working, ".spocr", "schema");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(Path.Combine(baseDir, "procedures"));
        Directory.CreateDirectory(Path.Combine(baseDir, "tabletypes"));
        Directory.CreateDirectory(Path.Combine(baseDir, "functions"));
        return baseDir;
    }

    public void SaveExpanded(SchemaSnapshot snapshot)
    {
        if (snapshot == null) return;
        var baseDir = EnsureBaseDir();
        if (baseDir == null) return;

        // Cleanup: remove legacy monolithic snapshot files (Fingerprint.json) except index.json
        try
        {
            var legacyFiles = Directory.GetFiles(baseDir, "*.json", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetFileName(f) != "index.json" && !string.Equals(Path.GetFileName(f), "procedures", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var lf in legacyFiles)
            {
                try { File.Delete(lf); } catch { }
            }
        }
        catch { }

        // Delta detection: load existing files
        var procDir = Path.Combine(baseDir, "procedures");
        var existingProcFiles = Directory.GetFiles(procDir, "*.json", SearchOption.TopDirectoryOnly)
            .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);
        var procHashes = new List<FileHashEntry>();
        foreach (var proc in snapshot.Procedures ?? Enumerable.Empty<SnapshotProcedure>())
        {
            var fileName = $"{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(proc.Schema)}.{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(proc.Name)}.json";
            var path = Path.Combine(procDir, fileName);
            var rawJson = JsonSerializer.Serialize(proc, _jsonOptions);
            // Forwarding-Minimalismus: Entferne JSON-Flags & Columns aus ResultSets mit ExecSourceProcedureName
            var json = StripForwardedFlags(rawJson);
            var newHash = HashUtils.Sha256Hex(json).Substring(0, 16);
            bool needsWrite = true;
            if (existingProcFiles.TryGetValue(fileName, out var existingPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(existingPath);
                    var existingHash = HashUtils.Sha256Hex(existingJson).Substring(0, 16);
                    if (existingHash == newHash) needsWrite = false;
                }
                catch { }
            }
            if (needsWrite)
            {
                File.WriteAllText(path, json);
            }
            procHashes.Add(new FileHashEntry
            {
                Name = proc.Name,
                Schema = proc.Schema,
                File = fileName,
                Hash = newHash
            });
            existingProcFiles.Remove(fileName);
        }
        // Remove orphaned procedure files
        foreach (var orphan in existingProcFiles.Values)
        {
            try { File.Delete(orphan); } catch { }
        }

        var ttDir = Path.Combine(baseDir, "tabletypes");
        var existingTtFiles = Directory.GetFiles(ttDir, "*.json", SearchOption.TopDirectoryOnly)
            .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);
        var ttHashes = new List<FileHashEntry>();
        foreach (var udtt in snapshot.UserDefinedTableTypes ?? Enumerable.Empty<SnapshotUdtt>())
        {
            var fileName = $"{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(udtt.Schema)}.{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(udtt.Name)}.json";
            var path = Path.Combine(ttDir, fileName);
            var json = JsonSerializer.Serialize(udtt, _jsonOptions);
            var newHash = HashUtils.Sha256Hex(json).Substring(0, 16);
            bool needsWrite = true;
            if (existingTtFiles.TryGetValue(fileName, out var existingPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(existingPath);
                    var existingHash = HashUtils.Sha256Hex(existingJson).Substring(0, 16);
                    if (existingHash == newHash) needsWrite = false;
                }
                catch { }
            }
            if (needsWrite)
            {
                File.WriteAllText(path, json);
            }
            ttHashes.Add(new FileHashEntry
            {
                Name = udtt.Name,
                Schema = udtt.Schema,
                File = fileName,
                Hash = newHash
            });
            existingTtFiles.Remove(fileName);
        }
        foreach (var orphan in existingTtFiles.Values)
        {
            try { File.Delete(orphan); } catch { }
        }

        // Functions (preview)
        var fnDir = Path.Combine(baseDir, "functions");
        var existingFnFiles = Directory.GetFiles(fnDir, "*.json", SearchOption.TopDirectoryOnly)
            .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);
        var fnHashes = new List<FileHashEntry>();
        foreach (var fn in snapshot.Functions ?? Enumerable.Empty<SnapshotFunction>())
        {
            var fileName = $"{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(fn.Schema)}.{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(fn.Name)}.json";
            var path = Path.Combine(fnDir, fileName);
                // Prune: leere Columns Liste bei nicht-TVF oder leerer TVF -> null (nicht schreiben)
                if (fn.Columns != null && fn.Columns.Count == 0) fn.Columns = null;
                var json = JsonSerializer.Serialize(fn, _jsonOptions);
            var newHash = HashUtils.Sha256Hex(json).Substring(0, 16);
            bool needsWrite = true;
            if (existingFnFiles.TryGetValue(fileName, out var existingPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(existingPath);
                    var existingHash = HashUtils.Sha256Hex(existingJson).Substring(0, 16);
                    if (existingHash == newHash) needsWrite = false;
                }
                catch { }
            }
            if (needsWrite)
            {
                File.WriteAllText(path, json);
            }
            fnHashes.Add(new FileHashEntry
            {
                Name = fn.Name,
                Schema = fn.Schema,
                File = fileName,
                Hash = newHash
            });
            existingFnFiles.Remove(fileName);
        }
        foreach (var orphan in existingFnFiles.Values)
        {
            try { File.Delete(orphan); } catch { }
        }

        // Write index.json only when content changed – no GeneratedUtc to ensure deterministic diffs
        // Deterministic ordering to avoid diff noise
        procHashes = procHashes
            .OrderBy(p => p.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ttHashes = ttHashes
            .OrderBy(p => p.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var fnHashesOrdered = fnHashes
            .OrderBy(f => f.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = new ExpandedSnapshotIndex
        {
            SchemaVersion = snapshot.SchemaVersion,
            Fingerprint = snapshot.Fingerprint,
            Parser = snapshot.Parser,
            Stats = snapshot.Stats,
            Procedures = procHashes,
            TableTypes = ttHashes,
            FunctionsVersion = snapshot.FunctionsVersion,
            Functions = fnHashesOrdered
        };
        var indexPath = Path.Combine(baseDir, "index.json");
        var indexJson = JsonSerializer.Serialize(index, _jsonOptions);
        bool writeIndex = true;
        if (File.Exists(indexPath))
        {
            try
            {
                var existing = File.ReadAllText(indexPath);
                if (existing == indexJson)
                {
                    writeIndex = false; // keine Änderung
                }
            }
            catch { }
        }
        if (writeIndex)
        {
            File.WriteAllText(indexPath, indexJson);
        }
        try { Console.Out.WriteLine($"[snapshot-functions] count={fnHashes.Count}"); } catch { }
    }

    // Angepasst: Erhalte JSON-Flags & Columns auch bei forwardeten ResultSets (nur minimale Normalisierung möglich)
    private static string StripForwardedFlags(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            using var ms = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("ResultSets"))
                    {
                        if (prop.Value.ValueKind != JsonValueKind.Array)
                        {
                            writer.WritePropertyName(prop.Name);
                            prop.Value.WriteTo(writer);
                            continue;
                        }
                        writer.WritePropertyName("ResultSets");
                        writer.WriteStartArray();
                        foreach (var rs in prop.Value.EnumerateArray())
                        {
                            // Erkennen eines reinen Platzhalters: ExecSource* gesetzt, keine Columns, kein ReturnsJson
                            bool isPlaceholder = false;
                            string execSchema = null;
                            string execProc = null;
                            if (rs.ValueKind == JsonValueKind.Object)
                            {
                                bool hasColumns = false;
                                bool hasReturnsJson = false;
                                foreach (var rp in rs.EnumerateObject())
                                {
                                    if (rp.NameEquals("ExecSourceSchemaName")) execSchema = rp.Value.GetString();
                                    if (rp.NameEquals("ExecSourceProcedureName")) execProc = rp.Value.GetString();
                                    if (rp.NameEquals("Columns"))
                                    {
                                        if (rp.Value.ValueKind == JsonValueKind.Array && rp.Value.GetArrayLength() > 0) hasColumns = true;
                                    }
                                    if (rp.NameEquals("ReturnsJson"))
                                    {
                                        if (rp.Value.ValueKind == JsonValueKind.True) hasReturnsJson = true;
                                    }
                                }
                                isPlaceholder = !string.IsNullOrEmpty(execSchema) && !string.IsNullOrEmpty(execProc) && !hasColumns && !hasReturnsJson;
                            }
                            if (isPlaceholder)
                            {
                                writer.WriteStartObject();
                                if (!string.IsNullOrEmpty(execSchema))
                                {
                                    writer.WriteString("ExecSourceSchemaName", execSchema);
                                }
                                if (!string.IsNullOrEmpty(execProc))
                                {
                                    writer.WriteString("ExecSourceProcedureName", execProc);
                                }
                                writer.WriteEndObject();
                            }
                            else
                            {
                                // Lokale oder forwarded echte Sets mit zusätzlicher Pruning-Logik schreiben
                                writer.WriteStartObject();
                                foreach (var rsProp in rs.EnumerateObject())
                                {
                                    // Prune HasSelectStar when false
                                    if (rsProp.NameEquals("HasSelectStar") && rsProp.Value.ValueKind == JsonValueKind.False)
                                        continue;
                                    if (rsProp.NameEquals("Columns"))
                                    {
                                        if (rsProp.Value.ValueKind == JsonValueKind.Array)
                                        {
                                            int len = rsProp.Value.GetArrayLength();
                                            if (len == 0) continue; // drop empty array
                                            writer.WritePropertyName("Columns");
                                            writer.WriteStartArray();
                                            foreach (var col in rsProp.Value.EnumerateArray())
                                            {
                                                if (col.ValueKind != JsonValueKind.Object)
                                                {
                                                    col.WriteTo(writer); // unexpected kind, just write
                                                    continue;
                                                }
                                                writer.WriteStartObject();
                                                foreach (var colProp in col.EnumerateObject())
                                                {
                                                    // Drop IsNullable when false (default)
                                                    if (colProp.NameEquals("IsNullable") && colProp.Value.ValueKind == JsonValueKind.False)
                                                        continue;
                                                    // Also drop empty Columns arrays (defensive) if reached here (should already be handled)
                                                    if (colProp.NameEquals("Columns") && colProp.Value.ValueKind == JsonValueKind.Array && colProp.Value.GetArrayLength() == 0)
                                                        continue;
                                                    // Drop IsNestedJson when ReturnsJson true (redundant) already handled earlier in pipeline, but double-prune for safety
                                                    if (colProp.NameEquals("IsNestedJson"))
                                                    {
                                                        // Need to look ahead if ReturnsJson property exists with true
                                                        bool returnsJsonTrue = col.EnumerateObject().Any(p => p.NameEquals("ReturnsJson") && p.Value.ValueKind == JsonValueKind.True);
                                                        if (returnsJsonTrue) continue;
                                                    }
                                                    writer.WritePropertyName(colProp.Name);
                                                    colProp.Value.WriteTo(writer);
                                                }
                                                writer.WriteEndObject();
                                            }
                                            writer.WriteEndArray();
                                        }
                                        continue; // handled
                                    }
                                    writer.WritePropertyName(rsProp.Name);
                                    rsProp.Value.WriteTo(writer);
                                }
                                writer.WriteEndObject();
                            }
                        }
                        writer.WriteEndArray();
                    }
                    else
                    {
                        writer.WritePropertyName(prop.Name);
                        prop.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch { return json; }
    }

    public SchemaSnapshot LoadExpanded()
    {
        var baseDir = EnsureBaseDir();
        if (baseDir == null) return null;
        var indexPath = Path.Combine(baseDir, "index.json");
        if (!File.Exists(indexPath)) return null;
        ExpandedSnapshotIndex index;
        try { index = JsonSerializer.Deserialize<ExpandedSnapshotIndex>(File.ReadAllText(indexPath), _jsonOptions); } catch { return null; }
        if (index == null) return null;

        var snapshot = new SchemaSnapshot
        {
            SchemaVersion = index.SchemaVersion,
            Fingerprint = index.Fingerprint,
            Parser = index.Parser,
            Stats = index.Stats,
            Procedures = new List<SnapshotProcedure>(),
            UserDefinedTableTypes = new List<SnapshotUdtt>(),
            // Wichtig: Schemas wird aktuell nicht aus index.json rekonstruiert – wir leiten sie später ab.
            Schemas = new List<SnapshotSchema>()
        };

        // Load procedures
        foreach (var p in index.Procedures ?? Enumerable.Empty<FileHashEntry>())
        {
            var path = Path.Combine(baseDir, "procedures", p.File);
            if (!File.Exists(path)) continue;
            try
            {
                var proc = JsonSerializer.Deserialize<SnapshotProcedure>(File.ReadAllText(path), _jsonOptions);
                if (proc != null) snapshot.Procedures.Add(proc);
            }
            catch { /* ignore single file errors */ }
        }
        // Load table types
        foreach (var t in index.TableTypes ?? Enumerable.Empty<FileHashEntry>())
        {
            var path = Path.Combine(baseDir, "tabletypes", t.File);
            if (!File.Exists(path)) continue;
            try
            {
                var udtt = JsonSerializer.Deserialize<SnapshotUdtt>(File.ReadAllText(path), _jsonOptions);
                if (udtt != null) snapshot.UserDefinedTableTypes.Add(udtt);
            }
            catch { }
        }
        // Load functions preview
        if (index.FunctionsVersion.HasValue)
        {
            snapshot.FunctionsVersion = index.FunctionsVersion;
            foreach (var f in index.Functions ?? Enumerable.Empty<FileHashEntry>())
            {
                var path = Path.Combine(baseDir, "functions", f.File);
                if (!File.Exists(path)) continue;
                try
                {
                    var fn = JsonSerializer.Deserialize<SnapshotFunction>(File.ReadAllText(path), _jsonOptions);
                    if (fn != null) snapshot.Functions.Add(fn);
                }
                catch { }
            }
        }
        // Schemas ableiten: Union aus allen Procedure- und UDTT-Schemata.
        try
        {
            var schemaNames = snapshot.Procedures.Select(p => p.Schema)
                .Concat(snapshot.UserDefinedTableTypes.Select(u => u.Schema))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
            foreach (var sn in schemaNames)
            {
                snapshot.Schemas.Add(new SnapshotSchema
                {
                    Name = sn,
                    // Status wird nicht persistiert; IgnoredSchemas steuern spätere Filterung.
                });
            }
        }
        catch { /* best effort */ }
        return snapshot;
    }

    // File-level sanitization now centralized in NameSanitizer.SanitizeForFile

    #region Index Models
    public sealed class ExpandedSnapshotIndex
    {
        public int SchemaVersion { get; set; }
        public string Fingerprint { get; set; }
        public SnapshotParserInfo Parser { get; set; }
        public SnapshotStats Stats { get; set; }
        public List<FileHashEntry> Procedures { get; set; } = new();
        public List<FileHashEntry> TableTypes { get; set; } = new();
        public int? FunctionsVersion { get; set; }
        public List<FileHashEntry> Functions { get; set; } = new();
    }

    public sealed class FileHashEntry
    {
        public string Schema { get; set; }
        public string Name { get; set; }
        public string File { get; set; }
        public string Hash { get; set; }
    }
    #endregion
}

internal static class HashUtils
{
    public static string Sha256Hex(string content)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content ?? string.Empty);
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SpocR.SpocRVNext.Models;

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
        // Neue Verzeichnisse (Prio 1): types, tables, views
        Directory.CreateDirectory(Path.Combine(baseDir, "types"));
        Directory.CreateDirectory(Path.Combine(baseDir, "tables"));
        Directory.CreateDirectory(Path.Combine(baseDir, "views"));
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
            var fileModel = BuildProcedureFileModel(proc);
            var rawJson = JsonSerializer.Serialize(fileModel, _jsonOptions);
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
        // Remove orphaned procedure files - but only if no procedure filter is active
        // When --procedure filter is set, we should not delete other procedures
        var procedureFilter = Environment.GetEnvironmentVariable("SPOCR_BUILD_PROCEDURES");
        bool hasProcedureFilter = !string.IsNullOrWhiteSpace(procedureFilter);

        if (!hasProcedureFilter)
        {
            // No filter active - safe to remove all orphaned files
            foreach (var orphan in existingProcFiles.Values)
            {
                try { File.Delete(orphan); } catch { }
            }
        }
        else
        {
            // Filter is active - only remove files that match the filter pattern
            var tokens = procedureFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(p => p.Trim())
                                       .Where(p => !string.IsNullOrEmpty(p))
                                       .ToList();

            var procedureFilterExact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var procedureFilterWildcard = new List<Regex>();

            foreach (var t in tokens)
            {
                if (t.Contains('*') || t.Contains('?'))
                {
                    // Convert wildcard to Regex: escape, then replace \* -> .*, \? -> .
                    var escaped = Regex.Escape(t);
                    var pattern = "^" + escaped.Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    try { procedureFilterWildcard.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)); } catch { }
                }
                else
                {
                    procedureFilterExact.Add(t);
                }
            }

            bool MatchesFilter(string fileName)
            {
                // Extract schema.name from fileName (remove .json extension and convert back from sanitized names)
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var parts = nameWithoutExt.Split('.');
                if (parts.Length != 2) return false;

                var fqName = $"{parts[0]}.{parts[1]}"; // Note: this assumes sanitized names match original names

                if (procedureFilterExact.Contains(fqName)) return true;
                foreach (var rx in procedureFilterWildcard)
                {
                    if (rx.IsMatch(fqName)) return true;
                }
                return false;
            }

            // Only remove orphaned files that match the active filter
            foreach (var orphan in existingProcFiles.Values)
            {
                var fileName = Path.GetFileName(orphan);
                if (MatchesFilter(fileName))
                {
                    try { File.Delete(orphan); } catch { }
                }
            }
        }

        var ttDir = Path.Combine(baseDir, "tabletypes");
        var existingTtFiles = Directory.GetFiles(ttDir, "*.json", SearchOption.TopDirectoryOnly)
            .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);
        var ttHashes = new List<FileHashEntry>();
        foreach (var udtt in snapshot.UserDefinedTableTypes ?? Enumerable.Empty<SnapshotUdtt>())
        {
            var fileName = $"{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(udtt.Schema)}.{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(udtt.Name)}.json";
            var path = Path.Combine(ttDir, fileName);
            // Hash im Snapshot entfernen – nur fachliche Struktur persistieren
            var cleanUdtt = new SnapshotUdtt
            {
                Schema = udtt.Schema,
                Name = udtt.Name,
                Columns = (udtt.Columns ?? new List<SnapshotUdttColumn>()).Select(c => new SnapshotUdttColumn
                {
                    Name = c.Name,
                    TypeRef = c.TypeRef,
                    IsNullable = c.IsNullable == true ? true : null,
                    MaxLength = (c.MaxLength.HasValue && c.MaxLength.Value > 0) ? c.MaxLength : null,
                    Precision = (c.Precision.HasValue && c.Precision.Value > 0) ? c.Precision : null,
                    Scale = (c.Scale.HasValue && c.Scale.Value > 0) ? c.Scale : null
                }).ToList()
            };
            var json = JsonSerializer.Serialize(cleanUdtt, _jsonOptions);
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
            // IsTableValued: false prunen (nur true behalten)
            if (fn.IsTableValued == false) fn.IsTableValued = null;
            // Return Felder bei JSON-Skalaren Funktionen entfernen
            if (fn.ReturnsJson == true)
            {
                fn.ReturnSqlType = null; // nicht persistieren
                fn.ReturnIsNullable = null;
                // ReturnMaxLength lassen wir vorerst stehen (kann zur Unterscheidung NVARCHAR(MAX) vs NVARCHAR(200) dienen), optional später prunen
            }
            // Leere Columns Liste -> null
            if (fn.Columns != null && fn.Columns.Count == 0) fn.Columns = null;
            // Parameter Pruning
            if (fn.Parameters != null && fn.Parameters.Count > 0)
            {
                foreach (var prm in fn.Parameters)
                {
                    if (prm == null) continue;
                    if (string.IsNullOrWhiteSpace(prm.TypeRef))
                    {
                        prm.TypeRef = CombineTypeRef(prm.TableTypeSchema, prm.TableTypeName);
                    }
                    if (string.IsNullOrWhiteSpace(prm.TypeRef))
                    {
                        prm.TypeRef = null;
                    }
                    else
                    {
                        prm.TypeRef = prm.TypeRef.Trim();
                    }
                    if (prm.IsOutput == false) prm.IsOutput = null;
                    if (prm.HasDefaultValue != true) prm.HasDefaultValue = null;
                    if (prm.IsNullable == false) prm.IsNullable = null;
                    if (prm.MaxLength.HasValue && prm.MaxLength.Value == 0) prm.MaxLength = null;
                }
            }
            // Columns (rekursiv) Pruning
            void PruneFnColumns(List<SnapshotFunctionColumn> cols)
            {
                if (cols == null) return;
                foreach (var c in cols.ToList())
                {
                    if (c == null) continue;
                    if (string.IsNullOrWhiteSpace(c.TypeRef))
                    {
                        c.TypeRef = null;
                    }
                    else
                    {
                        c.TypeRef = c.TypeRef.Trim();
                    }
                    if (c.IsNullable == false) c.IsNullable = null;
                    if (c.MaxLength.HasValue && c.MaxLength.Value == 0) c.MaxLength = null;
                    if (c.IsNestedJson == false) c.IsNestedJson = null; // nur true behalten
                    if (c.ReturnsJson == false) c.ReturnsJson = null;
                    if (c.ReturnsJsonArray == false) c.ReturnsJsonArray = null;
                    if (!string.IsNullOrWhiteSpace(c.JsonRootProperty)) c.JsonRootProperty = c.JsonRootProperty.Trim();
                    if (c.Columns != null && c.Columns.Count == 0) c.Columns = null;
                    if (c.Columns != null) PruneFnColumns(c.Columns);
                }
            }
            if (fn.Columns != null) PruneFnColumns(fn.Columns);
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

        // Build hashes for new artefact types (scalar UDTs, tables, views)
        var typesDir = Path.Combine(baseDir, "types");
        var tablesDir = Path.Combine(baseDir, "tables");
        var viewsDir = Path.Combine(baseDir, "views");
        Directory.CreateDirectory(typesDir);
        Directory.CreateDirectory(tablesDir);
        Directory.CreateDirectory(viewsDir);

        var typeHashes = new List<FileHashEntry>();
        foreach (var udt in snapshot.UserDefinedTypes ?? Enumerable.Empty<SnapshotUserDefinedType>())
        {
            var baseName = SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(udt.Name);
            var schemaName = SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(udt.Schema);
            // Base type file (schema.name.json)
            var fileName = $"{schemaName}.{baseName}.json";
            var path = Path.Combine(typesDir, fileName);
            var json = JsonSerializer.Serialize(udt, _jsonOptions);
            var newHash = HashUtils.Sha256Hex(json).Substring(0, 16);
            File.WriteAllText(path, json); // keine Delta-Optimierung nötig (klein)
            typeHashes.Add(new FileHashEntry { Schema = udt.Schema, Name = udt.Name, File = fileName, Hash = newHash });

            // Underscore NOT NULL alias (schema._name.json)
            try
            {
                var underscoreName = baseName.StartsWith("_") ? baseName : ("_" + baseName);
                var udtNotNull = new SnapshotUserDefinedType
                {
                    Schema = udt.Schema,
                    Name = underscoreName,
                    BaseSqlTypeName = udt.BaseSqlTypeName,
                    MaxLength = udt.MaxLength,
                    Precision = udt.Precision,
                    Scale = udt.Scale,
                    IsNullable = false
                };
                var fileNameUnderscore = $"{schemaName}.{underscoreName}.json";
                var pathUnderscore = Path.Combine(typesDir, fileNameUnderscore);
                var jsonUnderscore = JsonSerializer.Serialize(udtNotNull, _jsonOptions);
                var hashUnderscore = HashUtils.Sha256Hex(jsonUnderscore).Substring(0, 16);
                File.WriteAllText(pathUnderscore, jsonUnderscore);
                typeHashes.Add(new FileHashEntry { Schema = udt.Schema, Name = underscoreName, File = fileNameUnderscore, Hash = hashUnderscore });
            }
            catch { }
        }

        var tableHashes = new List<FileHashEntry>();
        foreach (var tbl in snapshot.Tables ?? Enumerable.Empty<SnapshotTable>())
        {
            var fileName = $"{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(tbl.Schema)}.{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(tbl.Name)}.json";
            var path = Path.Combine(tablesDir, fileName);
            // Spalten-Pruning analog Procedure Columns: false/null Werte entfernen
            foreach (var c in tbl.Columns ?? new List<SnapshotTableColumn>())
            {
                if (string.IsNullOrWhiteSpace(c.TypeRef))
                {
                    c.TypeRef = null;
                }
                else
                {
                    c.TypeRef = c.TypeRef.Trim();
                }
                if (c.IsNullable == false) c.IsNullable = null;
                if (c.IsIdentity == false) c.IsIdentity = null;
                if (c.MaxLength.HasValue && c.MaxLength.Value == 0) c.MaxLength = null;
                if (c.Precision.HasValue && c.Precision.Value == 0) c.Precision = null;
                if (c.Scale.HasValue && c.Scale.Value == 0) c.Scale = null;
            }
            var json = JsonSerializer.Serialize(tbl, _jsonOptions);
            var newHash = HashUtils.Sha256Hex(json).Substring(0, 16);
            File.WriteAllText(path, json);
            tableHashes.Add(new FileHashEntry { Schema = tbl.Schema, Name = tbl.Name, File = fileName, Hash = newHash });
        }

        var viewHashes = new List<FileHashEntry>();
        foreach (var vw in snapshot.Views ?? Enumerable.Empty<SnapshotView>())
        {
            var fileName = $"{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(vw.Schema)}.{SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(vw.Name)}.json";
            var path = Path.Combine(viewsDir, fileName);
            foreach (var c in vw.Columns ?? new List<SnapshotViewColumn>())
            {
                if (string.IsNullOrWhiteSpace(c.TypeRef))
                {
                    c.TypeRef = null;
                }
                else
                {
                    c.TypeRef = c.TypeRef.Trim();
                }
                if (c.IsNullable == false) c.IsNullable = null;
                if (c.MaxLength.HasValue && c.MaxLength.Value == 0) c.MaxLength = null;
                if (c.Precision.HasValue && c.Precision.Value == 0) c.Precision = null;
                if (c.Scale.HasValue && c.Scale.Value == 0) c.Scale = null;
            }
            var json = JsonSerializer.Serialize(vw, _jsonOptions);
            var newHash = HashUtils.Sha256Hex(json).Substring(0, 16);
            File.WriteAllText(path, json);
            viewHashes.Add(new FileHashEntry { Schema = vw.Schema, Name = vw.Name, File = fileName, Hash = newHash });
        }

        var index = new ExpandedSnapshotIndex
        {
            SchemaVersion = snapshot.SchemaVersion,
            Fingerprint = snapshot.Fingerprint,
            Parser = snapshot.Parser,
            Stats = snapshot.Stats,
            Procedures = procHashes,
            TableTypes = ttHashes,
            FunctionsVersion = snapshot.FunctionsVersion,
            Functions = fnHashesOrdered,
            UserDefinedTypes = typeHashes.OrderBy(h => h.Schema).ThenBy(h => h.Name).ToList(),
            Tables = tableHashes.OrderBy(h => h.Schema).ThenBy(h => h.Name).ToList(),
            Views = viewHashes.OrderBy(h => h.Schema).ThenBy(h => h.Name).ToList()
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
            UserDefinedTypes = new List<SnapshotUserDefinedType>(),
            Tables = new List<SnapshotTable>(),
            Views = new List<SnapshotView>(),
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
                var json = File.ReadAllText(path);
                var model = JsonSerializer.Deserialize<ProcedureFileModel>(json, _jsonOptions);
                if (model == null) continue;

                List<SnapshotInput> parameterList;
                if (model.Parameters != null)
                {
                    parameterList = new List<SnapshotInput>();
                    foreach (var parameter in model.Parameters)
                    {
                        if (parameter == null) continue;

                        var snapshotInput = new SnapshotInput
                        {
                            Name = parameter.Name ?? string.Empty,
                            IsOutput = parameter.IsOutput,
                            HasDefaultValue = parameter.HasDefaultValue,
                            TypeRef = parameter.TypeRef,
                            IsNullable = parameter.IsNullable,
                            MaxLength = parameter.MaxLength,
                            Precision = parameter.Precision,
                            Scale = parameter.Scale
                        };

                        var resolvedTypeRef = snapshotInput.TypeRef;
                        if (string.IsNullOrWhiteSpace(resolvedTypeRef))
                        {
                            resolvedTypeRef = CombineTypeRef(parameter.TableTypeSchema, parameter.TableTypeName)
                                ?? CombineTypeRef(parameter.TypeSchema, parameter.TypeName);

                            if (string.IsNullOrWhiteSpace(resolvedTypeRef) && !string.IsNullOrWhiteSpace(parameter.SqlTypeName))
                            {
                                var normalized = NormalizeSqlTypeName(parameter.SqlTypeName);
                                if (!string.IsNullOrWhiteSpace(normalized))
                                {
                                    resolvedTypeRef = CombineTypeRef("sys", normalized);
                                }
                            }
                        }

                        snapshotInput.TypeRef = resolvedTypeRef;

                        var (schema, name) = SplitTypeRef(resolvedTypeRef);
                        var kind = DetermineTypeRefKind(baseDir, schema, name, parameter.IsTableType);

                        if (kind == ParameterTypeRefKind.TableType || (!string.IsNullOrWhiteSpace(parameter.TableTypeSchema) && !string.IsNullOrWhiteSpace(parameter.TableTypeName)))
                        {
                            snapshotInput.TableTypeSchema = parameter.TableTypeSchema ?? schema;
                            snapshotInput.TableTypeName = parameter.TableTypeName ?? name;
                        }
                        else if (!string.IsNullOrWhiteSpace(schema) || !string.IsNullOrWhiteSpace(name))
                        {
                            snapshotInput.TypeSchema = parameter.TypeSchema ?? schema;
                            snapshotInput.TypeName = parameter.TypeName ?? name;
                        }
                        else
                        {
                            snapshotInput.TypeSchema = parameter.TypeSchema;
                            snapshotInput.TypeName = parameter.TypeName;
                        }

                        parameterList.Add(snapshotInput);
                    }
                }
                else
                {
                    parameterList = model.Inputs ?? new List<SnapshotInput>();
                }
                var procedure = new SnapshotProcedure
                {
                    Schema = model.Schema,
                    Name = model.Name,
                    Inputs = parameterList,
                    ResultSets = model.ResultSets ?? new List<SnapshotResultSet>()
                };
                snapshot.Procedures.Add(procedure);
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
        // Load scalar user-defined types
        foreach (var typeEntry in index.UserDefinedTypes ?? Enumerable.Empty<FileHashEntry>())
        {
            var path = Path.Combine(baseDir, "types", typeEntry.File);
            if (!File.Exists(path)) continue;
            try
            {
                var udt = JsonSerializer.Deserialize<SnapshotUserDefinedType>(File.ReadAllText(path), _jsonOptions);
                if (udt != null) snapshot.UserDefinedTypes.Add(udt);
            }
            catch { }
        }
        // Load tables
        foreach (var tableEntry in index.Tables ?? Enumerable.Empty<FileHashEntry>())
        {
            var path = Path.Combine(baseDir, "tables", tableEntry.File);
            if (!File.Exists(path)) continue;
            try
            {
                var table = JsonSerializer.Deserialize<SnapshotTable>(File.ReadAllText(path), _jsonOptions);
                if (table == null)
                {
                    continue;
                }

                table.Columns ??= new List<SnapshotTableColumn>();
                snapshot.Tables.Add(table);
            }
            catch
            {
                // ignore individual table load failures
            }
        }
        // Load views
        foreach (var viewEntry in index.Views ?? Enumerable.Empty<FileHashEntry>())
        {
            var path = Path.Combine(baseDir, "views", viewEntry.File);
            if (!File.Exists(path)) continue;
            try
            {
                var view = JsonSerializer.Deserialize<SnapshotView>(File.ReadAllText(path), _jsonOptions);
                if (view == null)
                {
                    continue;
                }

                view.Columns ??= new List<SnapshotViewColumn>();
                snapshot.Views.Add(view);
            }
            catch
            {
                // ignore individual view load failures
            }
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
                .Concat(snapshot.Tables.Select(t => t.Schema))
                .Concat(snapshot.Views.Select(v => v.Schema))
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

    private static ProcedureFileModel BuildProcedureFileModel(SnapshotProcedure procedure)
    {
        var model = new ProcedureFileModel
        {
            Schema = procedure?.Schema ?? string.Empty,
            Name = procedure?.Name ?? string.Empty,
            ResultSets = procedure?.ResultSets ?? new List<SnapshotResultSet>(),
            Parameters = new List<ParameterFileModel>()
        };

        if (procedure?.Inputs != null)
        {
            foreach (var input in procedure.Inputs)
            {
                if (input == null) continue;
                model.Parameters!.Add(BuildParameterFileModel(input));
            }
        }

        return model;
    }

    private static ParameterFileModel BuildParameterFileModel(SnapshotInput input)
    {
        if (input == null)
        {
            return new ParameterFileModel();
        }

        var parameter = new ParameterFileModel
        {
            Name = input.Name ?? string.Empty,
            IsOutput = input.IsOutput == true ? true : null,
            HasDefaultValue = input.HasDefaultValue == true ? true : null,
            TableTypeSchema = input.TableTypeSchema,
            TableTypeName = input.TableTypeName,
            TypeSchema = input.TypeSchema,
            TypeName = input.TypeName
        };

        var tableTypeRef = CombineTypeRef(input.TableTypeSchema, input.TableTypeName);
        var scalarTypeRef = CombineTypeRef(input.TypeSchema, input.TypeName);

        var resolvedTypeRef = input.TypeRef;
        if (string.IsNullOrWhiteSpace(resolvedTypeRef))
        {
            resolvedTypeRef = !string.IsNullOrWhiteSpace(tableTypeRef)
                ? tableTypeRef
                : (!string.IsNullOrWhiteSpace(scalarTypeRef) ? scalarTypeRef : null);
        }

        if (string.IsNullOrWhiteSpace(resolvedTypeRef) && !string.IsNullOrWhiteSpace(input.TypeName))
        {
            var normalized = NormalizeSqlTypeName(input.TypeName);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                var schemaComponent = string.IsNullOrWhiteSpace(input.TypeSchema) ? "sys" : input.TypeSchema;
                resolvedTypeRef = CombineTypeRef(schemaComponent, normalized);
            }
        }

        parameter.TypeRef = resolvedTypeRef;

        if (!string.IsNullOrWhiteSpace(parameter.TableTypeSchema) && !string.IsNullOrWhiteSpace(parameter.TableTypeName))
        {
            parameter.IsTableType = true;
        }

        if (input.IsNullable == true)
        {
            parameter.IsNullable = true;
        }
        if (input.MaxLength.HasValue && input.MaxLength.Value > 0)
        {
            parameter.MaxLength = input.MaxLength;
        }
        if (input.Precision.HasValue && input.Precision.Value > 0)
        {
            parameter.Precision = input.Precision;
        }
        if (input.Scale.HasValue && input.Scale.Value > 0)
        {
            parameter.Scale = input.Scale;
        }

        return parameter;
    }

    private static string? CombineTypeRef(string? schema, string? name)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name)) return null;
        return string.Concat(schema.Trim(), ".", name.Trim());
    }

    private static (string? Schema, string? Name) SplitTypeRef(string? typeRef)
    {
        if (string.IsNullOrWhiteSpace(typeRef)) return (null, null);
        var parts = typeRef.Trim().Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            return (string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0], string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1]);
        }

        return (null, string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0]);
    }

    private static string? NormalizeSqlTypeName(string? sqlTypeName)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeName)) return null;
        return sqlTypeName.Trim().ToLowerInvariant();
    }

    private static ParameterTypeRefKind DetermineTypeRefKind(string baseDir, string? schema, string? name, bool? legacyHint)
    {
        if (legacyHint == true) return ParameterTypeRefKind.TableType;
        if (legacyHint == false) return ParameterTypeRefKind.Scalar;
        if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name)) return ParameterTypeRefKind.Unknown;

        var sanitizedSchema = SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(schema);
        var sanitizedName = SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeForFile(name);

        var tableTypePath = Path.Combine(baseDir, "tabletypes", $"{sanitizedSchema}.{sanitizedName}.json");
        if (File.Exists(tableTypePath))
        {
            return ParameterTypeRefKind.TableType;
        }

        var scalarTypePath = Path.Combine(baseDir, "types", $"{sanitizedSchema}.{sanitizedName}.json");
        if (File.Exists(scalarTypePath))
        {
            return ParameterTypeRefKind.Scalar;
        }

        return ParameterTypeRefKind.Scalar;
    }

    private enum ParameterTypeRefKind
    {
        Unknown = 0,
        Scalar,
        TableType
    }

    // File-level sanitization now centralized in NameSanitizer.SanitizeForFile

    private sealed class ProcedureFileModel
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<ParameterFileModel>? Parameters { get; set; }
        public List<SnapshotInput>? Inputs { get; set; }
        public List<SnapshotResultSet>? ResultSets { get; set; }
    }

    private sealed class ParameterFileModel
    {
        public string Name { get; set; } = string.Empty;
        public string? TypeRef { get; set; }
        public bool? IsTableType { get; set; }
        public string? SqlTypeName { get; set; }
        public bool? IsNullable { get; set; }
        public int? MaxLength { get; set; }
        public string? BaseSqlTypeName { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public string? TableTypeSchema { get; set; }
        public string? TableTypeName { get; set; }
        public string? TypeSchema { get; set; }
        public string? TypeName { get; set; }
        public bool? IsOutput { get; set; }
        public bool? HasDefaultValue { get; set; }
    }

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
        // Neue Kategorien (optional / leer für Kompatibilität mit älteren index.json Versionen)
        // Umbenennung: UserDefinedTypes statt Types (alias scalar UDTs)
        public List<FileHashEntry> UserDefinedTypes { get; set; } = new();
        public List<FileHashEntry> Tables { get; set; } = new();
        public List<FileHashEntry> Views { get; set; } = new();
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

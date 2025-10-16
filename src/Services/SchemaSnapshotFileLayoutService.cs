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
            var fileName = $"{Sanitize(proc.Schema)}.{Sanitize(proc.Name)}.json";
            var path = Path.Combine(procDir, fileName);
            var json = JsonSerializer.Serialize(proc, _jsonOptions);
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
                Hash = newHash,
                Unchanged = !needsWrite
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
            var fileName = $"{Sanitize(udtt.Schema)}.{Sanitize(udtt.Name)}.json";
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
                Hash = newHash,
                Unchanged = !needsWrite
            });
            existingTtFiles.Remove(fileName);
        }
        foreach (var orphan in existingTtFiles.Values)
        {
            try { File.Delete(orphan); } catch { }
        }

    // Write index.json only when content changed – no GeneratedUtc to ensure deterministic diffs
        var index = new ExpandedSnapshotIndex
        {
            SchemaVersion = snapshot.SchemaVersion,
            Fingerprint = snapshot.Fingerprint,
            Parser = snapshot.Parser,
            Stats = snapshot.Stats,
            Procedures = procHashes,
            TableTypes = ttHashes
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
            UserDefinedTableTypes = new List<SnapshotUdtt>()
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
        return snapshot;
    }

    private static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "_";
    // Allowed chars: a-zA-Z0-9._- ; everything else -> '_' ; collapse multiple underscores
        var chars = raw.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray();
        var sanitized = new string(chars);
        while (sanitized.Contains("__")) sanitized = sanitized.Replace("__", "_");
        return sanitized.Trim('_');
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
    }

    public sealed class FileHashEntry
    {
        public string Schema { get; set; }
        public string Name { get; set; }
        public string File { get; set; }
        public string Hash { get; set; }
        public bool Unchanged { get; set; }
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.SpocRVNext.Models;
using SpocR.Utils;

namespace SpocR.SpocRVNext.Services;

/// <summary>
/// Very lightweight local metadata cache. Not committed to source control.
/// Stores per stored procedure last known ModifiedTicks to allow skipping expensive detail loading.
/// </summary>
public interface ILocalCacheService
{
    ProcedureCacheSnapshot Load(string fingerprint);
    void Save(string fingerprint, ProcedureCacheSnapshot snapshot);
}

public class LocalCacheService : ILocalCacheService
{
    private string _rootDir; // lazily resolved based on working directory
    private string _lastWorkingDir;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LocalCacheService() { }

    private void EnsureRoot()
    {
        var working = DirectoryUtils.GetWorkingDirectory();
        if (string.IsNullOrEmpty(working)) return; // nothing we can do yet
        if (_rootDir == null || !string.Equals(_lastWorkingDir, working, StringComparison.OrdinalIgnoreCase))
        {
            var dotDir = Path.Combine(working, ".spocr");
            var candidate = Path.Combine(dotDir, "cache");
            try { Directory.CreateDirectory(candidate); } catch { /* ignore */ }
            _rootDir = candidate;
            _lastWorkingDir = working;
        }
    }

    private string GetPath(string fingerprint)
    {
        EnsureRoot();
        return _rootDir == null ? null : Path.Combine(_rootDir, $"{fingerprint}.json");
    }

    public ProcedureCacheSnapshot Load(string fingerprint)
    {
        try
        {
            var path = GetPath(fingerprint);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProcedureCacheSnapshot>(json, _jsonOptions);
        }
        catch { return null; }
    }

    public void Save(string fingerprint, ProcedureCacheSnapshot snapshot)
    {
        try
        {
            var path = GetPath(fingerprint);
            if (string.IsNullOrEmpty(path)) return; // not initialized
            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch { /* ignore */ }
    }
}

public class ProcedureCacheSnapshot
{
    public string Fingerprint { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<ProcedureCacheEntry> Procedures { get; set; } = new();

    public long? GetModifiedTicks(string schema, string name)
        => Procedures.FirstOrDefault(p => p.Schema == schema && p.Name == name)?.ModifiedTicks;
}

public class ProcedureCacheEntry
{
    public string Schema { get; set; }
    public string Name { get; set; }
    public long ModifiedTicks { get; set; }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.Models;

namespace SpocR.Services;

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
    private readonly string _rootDir;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LocalCacheService()
    {
        // New standard location: projectRoot/.spocr/cache (ignored from VCS)
        var dotDir = Path.Combine(Environment.CurrentDirectory, ".spocr");
        _rootDir = Path.Combine(dotDir, "cache");
        Directory.CreateDirectory(_rootDir);
    }

    private string GetPath(string fingerprint) => Path.Combine(_rootDir, $"{fingerprint}.json");

    public ProcedureCacheSnapshot Load(string fingerprint)
    {
        try
        {
            var path = GetPath(fingerprint);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProcedureCacheSnapshot>(json, _jsonOptions);
        }
        catch
        {
            return null; // fail silent, act like no cache
        }
    }

    public void Save(string fingerprint, ProcedureCacheSnapshot snapshot)
    {
        try
        {
            var path = GetPath(fingerprint);
            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore persistence errors
        }
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
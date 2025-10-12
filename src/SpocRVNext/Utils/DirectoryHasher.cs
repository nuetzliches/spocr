using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace SpocR.SpocRVNext.Utils;

/// <summary>
/// Computes SHA256 hashes for all files in a directory tree (deterministic ordering) and can emit a manifest.
/// </summary>
public static class DirectoryHasher
{
    public sealed record FileHash(string RelativePath, string Sha256);
    public sealed record Manifest(string Root, string Algorithm, IReadOnlyList<FileHash> Files, string AggregateSha256);

    public static Manifest HashDirectory(string root, Func<string, bool>? fileFilter = null)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Where(p => fileFilter?.Invoke(p) != false)
            .ToList();

        var entries = new List<FileHash>(files.Count);
        using var agg = SHA256.Create();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            var hash = ComputeFileHash(file);
            entries.Add(new FileHash(rel, hash));
            // Feed into aggregate in stable way
            var line = System.Text.Encoding.UTF8.GetBytes(rel + ":" + hash + "\n");
            agg.TransformBlock(line, 0, line.Length, null, 0);
        }
        agg.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var aggregate = Convert.ToHexString(agg.Hash!).ToLowerInvariant();
        return new Manifest(Path.GetFullPath(root), "SHA256", entries, aggregate);
    }

    public static void WriteManifest(Manifest manifest, string outputFile)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        File.WriteAllText(outputFile, json);
    }

    private static string ComputeFileHash(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
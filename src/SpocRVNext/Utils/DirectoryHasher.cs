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
        // For source files, strip volatile timestamp lines (Generated at ...)
        bool isText = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        if (!isText)
        {
            using var shaBin = SHA256.Create();
            using var fsBin = File.OpenRead(path);
            var hBin = shaBin.ComputeHash(fsBin);
            return Convert.ToHexString(hBin).ToLowerInvariant();
        }
        var lines = File.ReadAllLines(path)
            .Where(l => !l.Contains("Generated at ")) // ignore timestamp line
            .ToArray();
        using var sha = SHA256.Create();
        foreach (var line in lines)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(line + "\n");
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}
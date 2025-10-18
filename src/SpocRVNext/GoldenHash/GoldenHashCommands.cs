using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpocR.SpocRVNext.GoldenHash;

internal static class GoldenHashCommands
{
    internal sealed record GoldenResult(string Message, int ExitCode);

    private const string ManifestFile = "debug/golden-hash.json";
    private static readonly string[] RelevantFolders = new[] { "Samples", "Outputs", "SpocR" }; // vNext output roots in sample

    public static GoldenResult WriteGolden(string root)
    {
        try
        {
            var (hash, files) = ComputeHash(root);
            var manifest = new
            {
                hash,
                algorithm = "SHA256",
                generatedUtc = DateTime.UtcNow.ToString("o"),
                files
            };
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            var manifestPath = Path.Combine(root, ManifestFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, json);
            return new GoldenResult($"[golden] manifest written: {manifestPath}\n[golden] hash={hash} files={files.Count}", 0);
        }
        catch (Exception ex)
        {
            return new GoldenResult($"[golden][error] write failed: {ex.Message}", 1);
        }
    }

    public static GoldenResult VerifyGolden(string root)
    {
        var manifestPath = Path.Combine(root, ManifestFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifestPath))
        {
            return new GoldenResult($"[golden][warn] manifest missing: {manifestPath} (run write-golden first)", 0);
        }
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var expected = doc.RootElement.GetProperty("hash").GetString();
            var (currentHash, files) = ComputeHash(root);
            if (string.Equals(expected, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                return new GoldenResult($"[golden] match hash={currentHash} files={files.Count}", 0);
            }
            // relaxed vs strict
            var strict = IsStrict();
            var exit = strict ? 21 : 0; // 21 reserved for diff/golden mismatch
            var changedMsg = $"[golden]{(strict ? "[strict]" : "[relaxed]")} DIFF expected={expected} current={currentHash}";
            return new GoldenResult(changedMsg, exit);
        }
        catch (Exception ex)
        {
            var strict = IsStrict();
            var exit = strict ? 22 : 0; // 22: verification error
            return new GoldenResult($"[golden]{(strict ? "[strict]" : string.Empty)} error verifying: {ex.Message}", exit);
        }
    }

    private static (string Hash, List<string> Files) ComputeHash(string root)
    {
        var all = new List<string>();
        foreach (var folder in RelevantFolders)
        {
            var dir = Path.Combine(root, "samples", "restapi", folder); // sample path
            if (!Directory.Exists(dir)) continue;
            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
            all.AddRange(files);
        }
        // normalize order
        var ordered = all.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        using var sha = SHA256.Create();
        foreach (var file in ordered)
        {
            var text = File.ReadAllText(file);
            text = Normalize(text);
            var bytes = Encoding.UTF8.GetBytes(text);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = BitConverter.ToString(sha.Hash!).Replace("-", "");
        // relative file paths (from root)
        var rel = ordered.Select(f => Path.GetRelativePath(root, f)).ToList();
        return (hash, rel);
    }

    private static string Normalize(string text)
    {
        // remove dynamic remarks timestamps: lines containing '<remarks>' with date-like pattern
        var sb = new StringBuilder();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("//") && trimmed.Contains("Generated at")) continue; // legacy pattern
            if (trimmed.Contains("<remarks>") && trimmed.Contains("Generated") && trimmed.Contains("UTC")) continue;
            sb.AppendLine(line.Replace("\r", string.Empty));
        }
        return sb.ToString();
    }

    private static bool IsStrict() =>
        string.Equals(Environment.GetEnvironmentVariable("SPOCR_STRICT_DIFF"), "1", StringComparison.Ordinal) ||
        string.Equals(Environment.GetEnvironmentVariable("SPOCR_STRICT_GOLDEN"), "1", StringComparison.Ordinal);
}
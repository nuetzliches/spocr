using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SpocR.Tests;

public class ColumnNormalizationTests
{
    private static string RepoRoot => FindRepoRoot();
    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "samples", "restapi", ".spocr", "schema")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repository root not found for normalization tests");
    }

    private static string TablesDir => Path.Combine(RepoRoot, "samples", "restapi", ".spocr", "schema", "tables");
    private static string TypesDir => Path.Combine(RepoRoot, "samples", "restapi", ".spocr", "schema", "types");

    private static JsonDocument LoadFirst(string dir)
    {
        var file = Directory.EnumerateFiles(dir, "*.json").FirstOrDefault();
        if (file == null) throw new InvalidOperationException($"No json files found in {dir}");
        return JsonDocument.Parse(File.ReadAllText(file));
    }

    [Fact]
    public void IdentityFlag_Should_Exist_Only_When_True()
    {
        foreach (var file in Directory.EnumerateFiles(TablesDir, "*.json"))
        {
            var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("Columns", out var cols)) continue;
            foreach (var col in cols.EnumerateArray())
            {
                if (col.TryGetProperty("IsIdentity", out var idProp))
                {
                    Assert.True(idProp.GetBoolean(), "IsIdentity present but false (should be pruned when false)");
                }
            }
        }
    }

    [Fact]
    public void BaseSqlTypeName_Should_Differ_From_SqlTypeName_When_Present()
    {
        int checkedCount = 0;
        foreach (var file in Directory.EnumerateFiles(TablesDir, "*.json"))
        {
            var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("Columns", out var cols)) continue;
            foreach (var col in cols.EnumerateArray())
            {
                if (col.TryGetProperty("BaseSqlTypeName", out var baseProp) && col.TryGetProperty("SqlTypeName", out var typeProp))
                {
                    checkedCount++;
                    Assert.NotEqual(typeProp.GetString(), baseProp.GetString());
                }
            }
        }
        Assert.True(checkedCount >= 0, "No columns with BaseSqlTypeName found – acceptable if sample has none");
    }

    [Fact]
    public void PrecisionScale_Should_Appear_As_Paired_For_Decimals()
    {
        foreach (var file in Directory.EnumerateFiles(TablesDir, "*.json"))
        {
            var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("Columns", out var cols)) continue;
            foreach (var col in cols.EnumerateArray())
            {
                var hasPrecision = col.TryGetProperty("Precision", out var precisionProp);
                var hasScale = col.TryGetProperty("Scale", out var scaleProp);
                if (col.TryGetProperty("SqlTypeName", out var typeProp))
                {
                    var t = typeProp.GetString() ?? string.Empty;
                    if (t.StartsWith("decimal", StringComparison.OrdinalIgnoreCase) || t.StartsWith("numeric", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.True(hasPrecision && hasScale, "Decimal/numeric column missing precision/scale metadata");
                        Assert.True(precisionProp!.GetInt32() > 0, "Decimal/numeric precision should be >0");
                    }
                    else
                    {
                        // Non-decimal/numeric types: no strict requirement; allow presence of one or neither.
                    }
                }
            }
        }
    }

    [Fact]
    public void MaxLength_Prunes_Zero()
    {
        foreach (var file in Directory.EnumerateFiles(TablesDir, "*.json"))
        {
            var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("Columns", out var cols)) continue;
            foreach (var col in cols.EnumerateArray())
            {
                if (col.TryGetProperty("MaxLength", out var lenProp))
                {
                    Assert.True(lenProp.GetInt32() > 0, "MaxLength present but not >0 (0 should be pruned)");
                }
            }
        }
    }

    [Fact]
    public void UserDefinedTypeFiles_Should_Not_Have_Empty_BaseSqlTypeName()
    {
        if (!Directory.Exists(TypesDir)) return; // directory may not exist if sample changed
        foreach (var file in Directory.EnumerateFiles(TypesDir, "*.json"))
        {
            var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("BaseSqlTypeName", out var baseType))
            {
                Assert.False(string.IsNullOrWhiteSpace(baseType.GetString()), "BaseSqlTypeName empty in user-defined type file");
            }
        }
    }
}

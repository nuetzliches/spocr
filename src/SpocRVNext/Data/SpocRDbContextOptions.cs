using System.Text.Json;

namespace SpocR.SpocRVNext.Data;

public sealed class SpocRDbContextOptions
{
    public string? ConnectionString { get; set; }
    public int? CommandTimeout { get; set; }
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}

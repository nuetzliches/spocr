using System.Text.Json.Serialization;
using SpocR.SpocRVNext.Configuration;

namespace SpocR.SpocRVNext.Models;

public class RoleModel
{
    public RoleKindEnum Kind { get; set; } = RoleKindEnum.Default;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LibNamespace { get; set; }
}

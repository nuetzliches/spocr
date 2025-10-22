using System;

namespace RestApi.DataContext.Models.Samples;
/// <summary>Generated JSON model (legacy mode) – columns suppressed or not inferred.</summary>
/// <remarks>Raw JSON access still available via stored procedure Raw method. Upgrade to vNext mode for rich nested mapping.</remarks>
public class OrderListAsJson
{
    public int UserId { get; set; }
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public string OrderId { get; set; }
    public string TotalAmount { get; set; }
    public string PlacedAt { get; set; }
    public string Notes { get; set; }
}
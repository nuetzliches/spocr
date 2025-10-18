using System;

namespace RestApi.DataContext.Models.Samples;
public class OrderListAsJson
{
    public string UserId { get; set; }
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public string OrderId { get; set; }
    public string TotalAmount { get; set; }
    public string PlacedAt { get; set; }
    public string Notes { get; set; }
}
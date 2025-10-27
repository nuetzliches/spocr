using System;

namespace RestApi.DataContext.Models.Samples;
public class OrderListAsJson
{
    public int UserId { get; set; }
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public int OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime PlacedAt { get; set; }
    public string Notes { get; set; }
}
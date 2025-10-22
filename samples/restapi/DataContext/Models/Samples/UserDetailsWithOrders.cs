using System;

namespace RestApi.DataContext.Models.Samples;
public class UserDetailsWithOrders
{
    public int UserId { get; set; }
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Bio { get; set; }
}
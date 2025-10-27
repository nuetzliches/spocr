using System;

namespace RestApi.DataContext.Models.Samples;
public class UserOrderHierarchyJson
{
    public int UserId { get; set; }
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public dynamic Orders { get; set; }
}
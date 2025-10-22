using System;
using System.ComponentModel.DataAnnotations;

namespace RestApi.DataContext.TableTypes.Samples
{
    public class OrderImportTableType : ITableType
    {
        public int? UserId { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime PlacedAt { get; set; }
    }
}
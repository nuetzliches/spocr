using System;
using System.ComponentModel.DataAnnotations;

namespace RestApi.DataContext.TableTypes.Samples
{
    public class UserContactTableType : ITableType
    {
        public int? UserId { get; set; }

        [MaxLength(256)]
        public string Email { get; set; }

        [MaxLength(128)]
        public string DisplayName { get; set; }
    }
}
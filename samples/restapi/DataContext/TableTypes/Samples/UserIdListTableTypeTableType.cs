using System;
using System.ComponentModel.DataAnnotations;

namespace RestApi.DataContext.TableTypes.Samples
{
    public class UserIdListTableType : ITableType
    {
        public int? UserId { get; set; }
    }
}
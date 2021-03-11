using System;
using System.ComponentModel.DataAnnotations;

namespace Source.DataContext.TableTypes.Schema
{
    public class TableType : ITableType
    {
        public object Property { get; set; }
    }
}
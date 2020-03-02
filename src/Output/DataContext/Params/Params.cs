using System;
using System.ComponentModel.DataAnnotations;

namespace Source.DataContext.Params.Schema
{
    public class Params : IParams
    {
        public object Property { get; set; }
    }
}
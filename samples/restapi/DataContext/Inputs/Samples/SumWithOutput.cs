using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestApi.DataContext.Inputs.Samples
{
    public class SumWithOutputInput
    {
        [Obsolete("This empty contructor will be removed in vNext. Please use constructor with parameters.")]
        public SumWithOutputInput()
        {
        }

        public SumWithOutputInput(int? a, int? b)
        {
            A = a;
            B = b;
        }

        public int? A { get; set; }
        public int? B { get; set; }
        public int? Sum { get; set; }
        public bool? Success { get; set; }
    }
}
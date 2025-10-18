using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestApi.DataContext.Inputs.Samples
{
    public class OrderListByUserAsJsonInput
    {
        [Obsolete("This empty contructor will be removed in vNext. Please use constructor with parameters.")]
        public OrderListByUserAsJsonInput()
        {
        }

        public OrderListByUserAsJsonInput(int? userId)
        {
            UserId = userId;
        }

        public int? UserId { get; set; }
    }
}
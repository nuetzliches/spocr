using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestApi.DataContext.Inputs.Samples
{
    public class UserDetailsWithOrdersInput
    {
        [Obsolete("This empty contructor will be removed in vNext. Please use constructor with parameters.")]
        public UserDetailsWithOrdersInput()
        {
        }

        public UserDetailsWithOrdersInput(int? userId)
        {
            UserId = userId;
        }

        public int? UserId { get; set; }
    }
}
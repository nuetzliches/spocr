using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestApi.DataContext.Inputs.Samples
{
    public class UserFindInput
    {
        [Obsolete("This empty contructor will be removed in vNext. Please use constructor with parameters.")]
        public UserFindInput()
        {
        }

        public UserFindInput(int? userId)
        {
            UserId = userId;
        }

        public int? UserId { get; set; }
    }
}
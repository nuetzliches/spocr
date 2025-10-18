using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestApi.DataContext.Inputs.Samples
{
    public class UserBioUpdateInput
    {
        [Obsolete("This empty contructor will be removed in vNext. Please use constructor with parameters.")]
        public UserBioUpdateInput()
        {
        }

        public UserBioUpdateInput(int? userId, string bio)
        {
            UserId = userId;
            Bio = bio;
        }

        public int? UserId { get; set; }

        [MaxLength(512)]
        public string Bio { get; set; }
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestApi.DataContext.Inputs.Samples
{
    public class CreateUserWithOutputInput
    {
        [Obsolete("This empty contructor will be removed in vNext. Please use constructor with parameters.")]
        public CreateUserWithOutputInput()
        {
        }

        public CreateUserWithOutputInput(string displayName, string email)
        {
            DisplayName = displayName;
            Email = email;
        }

        [MaxLength(128)]
        public string DisplayName { get; set; }

        [MaxLength(256)]
        public string Email { get; set; }
        public int? UserId { get; set; }
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using RestApi.DataContext.TableTypes.Samples;

namespace RestApi.DataContext.Inputs.Samples
{
    public class UserContactSyncInput
    {
        [Obsolete("This empty contructor will be removed in vNext. Please use constructor with parameters.")]
        public UserContactSyncInput()
        {
        }

        public UserContactSyncInput(UserContactTableType contacts)
        {
            Contacts = contacts;
        }

        public UserContactTableType Contacts { get; set; }
    }
}
using System;

namespace SpocR.Attributes 
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public class WriteProtectedBySystem : Attribute 
    {
        public bool IsProtected { get; set; } = true;
    }
}
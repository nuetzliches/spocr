using System;

namespace SpocR.DataContext.Attributes
{
    internal class SqlFieldNameAttribute : Attribute
    {
        internal readonly string Name;
        internal SqlFieldNameAttribute(string name)
        {
            Name = name;
        }
    }
}
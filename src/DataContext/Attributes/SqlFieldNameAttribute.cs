using System;

namespace SpocR.DataContext.Attributes;

[AttributeUsage(AttributeTargets.Property)]
internal class SqlFieldNameAttribute : Attribute
{
    internal readonly string Name;
    internal SqlFieldNameAttribute(string name)
    {
        Name = name;
    }
}
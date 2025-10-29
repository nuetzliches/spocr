using System;

namespace SpocR.SpocRVNext.Data.Attributes;

[AttributeUsage(AttributeTargets.Property)]
internal sealed class SqlFieldNameAttribute : Attribute
{
    public SqlFieldNameAttribute(string name) => Name = name;

    public string Name { get; }
}

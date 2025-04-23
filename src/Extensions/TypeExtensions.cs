using System;
using System.Linq;

namespace SpocR.Extensions;

internal static class TypeExtensions
{
    public static string ToGenericTypeString(this Type t, bool simplifiedType = true, params Type[] arg)
    {
        if (t.IsGenericParameter || t.FullName == null) return ToSimplifiedName(t.Name);
        bool isGeneric = t.IsGenericType || t.FullName.Contains('`');
        bool isArray = !t.IsGenericType && t.FullName.Contains('`');
        Type genericType = t;
        while (genericType.IsNested && genericType.DeclaringType.GetGenericArguments().Length == t.GetGenericArguments().Length)
        {
            genericType = genericType.DeclaringType;
        }
        if (!isGeneric) return ToSimplifiedName(t.FullName.Replace('+', '.'));

        var arguments = arg.Length != 0 ? arg : t.GetGenericArguments();
        string genericTypeName = genericType.FullName;
        if (genericType.IsNested)
        {
            var argumentsToPass = arguments.Take(genericType.DeclaringType.GetGenericArguments().Length).ToArray();
            arguments = [.. arguments.Skip(argumentsToPass.Length)];
            genericTypeName = genericType.DeclaringType.ToGenericTypeString(simplifiedType, argumentsToPass) + "." + genericType.Name;
        }
        if (isArray)
        {
            genericTypeName = t.GetElementType().ToGenericTypeString() + "[]";
        }
        if (genericTypeName.Contains('`'))
        {
            genericTypeName = genericTypeName[..genericTypeName.IndexOf('`')];
            string genericArgs = string.Join(",", arguments.Select(a => a.ToGenericTypeString()).ToArray());

            genericTypeName = genericTypeName + "<" + genericArgs + ">";
            if (isArray) genericTypeName += "[]";
        }
        if (t != genericType)
        {
            genericTypeName += t.FullName.Replace(genericType.FullName, "").Replace('+', '.');
        }
        if (genericTypeName.Contains('[', StringComparison.CurrentCulture) && genericTypeName.IndexOf("]") != genericTypeName.IndexOf("[") + 1) genericTypeName = genericTypeName[..genericTypeName.IndexOf("[")];
        return ToSimplifiedName(genericTypeName);
    }

    private static string ToSimplifiedName(string typeName)
    {
        return typeName switch
        {
            "System.String" or "System.Nullable<string>" => "string",
            "System.Int32" => "int",
            "System.Nullable<int>" => "int?",
            "System.Boolean" => "bool",
            "System.Nullable<bool>" => "bool?",
            "System.Int64" => "long",
            "System.Nullable<long>" => "long?",
            "System.DateTime" => "DateTime",
            "System.Nullable<DateTime>" => "DateTime?",
            "System.Decimal" => "decimal",
            "System.Nullable<decimal>" => "decimal?",
            "System.Double" => "double",
            "System.Nullable<double>" => "double?",
            "System.Float" or "System.Single" => "float",
            "System.Nullable<float>" or "System.Nullable<single>" => "float?",
            "System.Byte[]" => "byte[]",
            "System.Guid" => "Guid",
            "System.Nullable<Guid>" => "Guid?",
            "System.Object" => "dynamic",
            _ => throw new ArgumentOutOfRangeException($"{nameof(TypeExtensions)}.{nameof(ToSimplifiedName)} - System.Type {typeName} not defined!"),
        };
    }
}

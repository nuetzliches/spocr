using System;
using System.Collections.Generic;
using System.Linq;

namespace SpocR.Extensions
{

    internal static class TypeExtensions
    {
        public static string ToGenericTypeString(this Type t, bool simplifiedType = true, params Type[] arg)
        {
            if (t.IsGenericParameter || t.FullName == null) return ToSimplifiedName(t.Name);//Generic argument stub
            bool isGeneric = t.IsGenericType || t.FullName.IndexOf('`') >= 0;//an array of generic types is not considered a generic type although it still have the genetic notation
            bool isArray = !t.IsGenericType && t.FullName.IndexOf('`') >= 0;
            Type genericType = t;
            while (genericType.IsNested && genericType.DeclaringType.GetGenericArguments().Count() == t.GetGenericArguments().Count())//Non generic class in a generic class is also considered in Type as being generic
            {
                genericType = genericType.DeclaringType;
            }
            if (!isGeneric) return ToSimplifiedName(t.FullName.Replace('+', '.'));

            var arguments = arg.Any() ? arg : t.GetGenericArguments();//if arg has any then we are in the recursive part, note that we always must take arguments from t, since only t (the last one) will actually have the constructed type arguments and all others will just contain the generic parameters
            string genericTypeName = genericType.FullName;
            if (genericType.IsNested)
            {
                var argumentsToPass = arguments.Take(genericType.DeclaringType.GetGenericArguments().Count()).ToArray();//Only the innermost will return the actual object and only from the GetGenericArguments directly on the type, not on the on genericDfintion, and only when all parameters including of the innermost are set
                arguments = arguments.Skip(argumentsToPass.Count()).ToArray();
                genericTypeName = genericType.DeclaringType.ToGenericTypeString(simplifiedType, argumentsToPass) + "." + genericType.Name;//Recursive
            }
            if (isArray)
            {
                genericTypeName = t.GetElementType().ToGenericTypeString() + "[]";//this should work even for multidimensional arrays
            }
            if (genericTypeName.IndexOf('`') >= 0)
            {
                genericTypeName = genericTypeName.Substring(0, genericTypeName.IndexOf('`'));
                string genericArgs = string.Join(",", arguments.Select(a => a.ToGenericTypeString()).ToArray());
                //Recursive
                genericTypeName = genericTypeName + "<" + genericArgs + ">";
                if (isArray) genericTypeName += "[]";
            }
            if (t != genericType)
            {
                genericTypeName += t.FullName.Replace(genericType.FullName, "").Replace('+', '.');
            }
            if (genericTypeName.IndexOf("[") >= 0 && genericTypeName.IndexOf("]") != genericTypeName.IndexOf("[") + 1) genericTypeName = genericTypeName.Substring(0, genericTypeName.IndexOf("["));//For a non generic class nested in a generic class we will still have the type parameters at the end 
            return ToSimplifiedName(genericTypeName);
        }

        private static string ToSimplifiedName(string typeName) {

            switch(typeName) {
                case "System.String":
                case "System.Nullable<string>":
                    return "string";
                case "System.Int32":
                    return "int";
                case "System.Nullable<int>":
                    return "int?";
                case "System.Boolean":
                    return "bool";
                case "System.Nullable<bool>":
                    return "bool?";
                case "System.Int64":
                    return "long";
                case "System.Nullable<long>":
                    return "long?";
                case "System.DateTime":
                    return "DateTime";
                case "System.Nullable<DateTime>":
                    return "DateTime?";
                case "System.Decimal":
                    return "decimal";
                case "System.Nullable<decimal>":
                    return "decimal?";
                case "System.Double":
                    return "double";
                case "System.Nullable<double>":
                    return "double?";
                case "System.Float":
                    return "float";
                case "System.Nullable<float>":
                    return "float?";
                case "System.Byte[]":
                    return "byte[]";
                case "System.Guid":
                    return "Guid";
                case "System.Nullable<Guid>":
                    return "Guid?";
                case "System.Object":
                    return "dynamic";
                default:
                    throw new ArgumentOutOfRangeException($"{nameof(TypeExtensions)}.{nameof(ToSimplifiedName)} - System.Type {typeName} not defined!");
            }
        }
    }
}
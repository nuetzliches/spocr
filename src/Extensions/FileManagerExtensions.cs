using System;
using System.Collections.Generic;
using System.Linq;

namespace SpocR.Extensions
{
    public static class FileManagerExtensions
    {
        public static T OverwriteWith<T>(this T target, T source) where T : class
        {
            if (source == null) return target;
            var properties = target.GetType().GetProperties();

            foreach (var property in properties.Where(p => p.CanWrite))
            {
                var propertyType = property.PropertyType;
                var sourceValue = property.GetValue(source, null);
                if (sourceValue == null
                    || (propertyType == typeof(string) && string.IsNullOrWhiteSpace(sourceValue.ToString())))
                {
                    continue;
                }

                if (propertyType.IsClass && !propertyType.IsCollection() && !propertyType.IsSealed) // !IsSealed: ignore Strings and other SystemTypes
                {
                    var targetValue = property.GetValue(target, null);
                    sourceValue = targetValue.OverwriteWith(sourceValue);
                }

                property.SetValue(target, sourceValue, null);
            }
            return target;
        }

        public static bool IsCollection(this Type propertyType)
        {
            return propertyType.GetInterface(typeof(IEnumerable<>).FullName) != null;
        }
    }

}
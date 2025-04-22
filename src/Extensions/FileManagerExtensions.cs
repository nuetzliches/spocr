using System;
using System.Collections;
using System.Linq;

namespace SpocR.Extensions;

public static class FileManagerExtensions
{
    public static T OverwriteWith<T>(this T target, T source) where T : class
    {
        if (source == null) return target;
        if (target == null) return source;

        var properties = target.GetType().GetProperties();

        foreach (var property in properties.Where(p => p.CanWrite))
        {
            var propertyType = property.PropertyType;
            var sourceValue = property.GetValue(source, null);

            // Skip null values and empty strings in source
            if (sourceValue == null ||
                (propertyType == typeof(string) && string.IsNullOrWhiteSpace(sourceValue.ToString())))
            {
                continue;
            }

            // Handle special case for collections
            if (propertyType.IsCollection())
            {
                // Only override collections if they have items
                if (sourceValue is IEnumerable sourceCollection && sourceCollection.Cast<object>().Any())
                {
                    property.SetValue(target, sourceValue, null);
                }
                continue;
            }

            // Handle nested objects (recursively)
            if (propertyType.IsClass && !propertyType.IsSealed) // !IsSealed: ignore Strings and other SystemTypes
            {
                var targetValue = property.GetValue(target, null);
                if (targetValue != null)
                {
                    sourceValue = targetValue.OverwriteWith(sourceValue);
                }
            }

            // Set the value
            property.SetValue(target, sourceValue, null);
        }
        return target;
    }

    public static bool IsCollection(this Type propertyType)
    {
        return propertyType != typeof(string) &&
               (typeof(IEnumerable).IsAssignableFrom(propertyType) ||
               propertyType.GetInterfaces().Any(i => i == typeof(IEnumerable)));
    }
}
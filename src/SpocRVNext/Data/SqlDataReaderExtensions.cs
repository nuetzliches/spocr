using System;
using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SpocR.SpocRVNext.Data.Attributes;

namespace SpocR.SpocRVNext.Data;

internal static class SqlDataReaderExtensions
{
    public static T ConvertToObject<T>(this SqlDataReader reader) where T : class, new()
    {
        var instance = new T();
        var properties = instance.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static p => p.SetMethod != null && p.SetMethod.IsPublic)
            .ToArray();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.IsDBNull(i))
            {
                continue;
            }

            var fieldName = reader.GetName(i);
            var property = properties.SingleOrDefault(p =>
                (p.GetCustomAttribute(typeof(SqlFieldNameAttribute)) as SqlFieldNameAttribute)?.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase) == true)
                ?? properties.SingleOrDefault(p => p.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            if (property == null)
            {
                continue;
            }

            var value = reader.GetValue(i);
            if (value is DateTime dateTime)
            {
                var useUtc = fieldName.EndsWith("Utc", StringComparison.InvariantCultureIgnoreCase);
                var ticks = dateTime.Ticks;
                value = useUtc ? new DateTime(ticks, DateTimeKind.Utc) : new DateTime(ticks, DateTimeKind.Local);
            }

            property.SetValue(instance, value);
        }

        return instance;
    }
}

using System;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Reflection;

namespace Source.DataContext
{
    public static class SqlDataReaderExtensions
    {
        public static T ConvertToObject<T>(this SqlDataReader reader) where T : class, new()
        {
            var obj = new T();
            var properties = obj.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(c => c.SetMethod != null && c.SetMethod.IsPublic).ToArray();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (reader.IsDBNull(i))
                {
                    continue;
                }
                var fieldName = reader.GetName(i);
                var propertie = properties.SingleOrDefault(p =>
                    p.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                propertie?.SetValue(obj, reader.GetValue(i));
            }

            return obj;
        }
    }
}
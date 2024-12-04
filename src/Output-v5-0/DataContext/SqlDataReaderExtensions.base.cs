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

                // SqlDataReader returns DateTime as Unspecified, but spocr is modifing it when storing -> src\Output-v5-0\DataContext\AppDbContext.base.cs #77
                if (value != null && value is DateTime)
                {
                    var useUtc = fieldName.EndsWith("Utc", StringComparison.InvariantCultureIgnoreCase);
                    var ticks = ((DateTime)value).Ticks;
                    if (useUtc) 
                    { 
                        value = new DateTime(ticks, DateTimeKind.Utc);
                    }
                    else 
                    {
                        value = new DateTime(ticks, DateTimeKind.Local);
                    }
                }

                propertie?.SetValue(obj, reader.GetValue(i));
            }

            return obj;
        }
    }
}
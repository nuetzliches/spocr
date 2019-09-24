using Microsoft.SqlServer.Server;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace Source.DataContext
{
    public static class SqlParameterExtensions
    {
        public static IEnumerable<SqlDataRecord> ToSqlParamCollection(this object value)
        {
            var collection = new List<SqlDataRecord>();
            var list = (IEnumerable)value;
            foreach (var row in list)
            {
                var rowType = row.GetType();
                var properties = rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                var metas = new List<SqlMetaData>();
                var values = new List<object>();
                foreach (var property in properties)
                {
                    var propVal = property.GetValue(row);
                    var propName = property.Name;
                    var sqlType = AppDbContext.GetSqlDbType(property.PropertyType);
                    if(sqlType == SqlDbType.NVarChar)
                    {
                        metas.Add(new SqlMetaData(propName, sqlType, propVal?.ToString().Length ?? 0));
                    }
                    else if (sqlType == SqlDbType.Decimal)
                    {
                        metas.Add(new SqlMetaData(propName, sqlType, 18, 4));
                    }
                    else
                    {
                        metas.Add(new SqlMetaData(propName, sqlType));
                    }
                    values.Add(propVal);
                }
                var record = new SqlDataRecord(metas.ToArray());
                record.SetValues(values.ToArray());
                collection.Add(record);
            }

            // If there are no records in the collection, use a null reference for the value instead.
            return collection.Count > 0
                ? collection
                : null;
        }
    }
}
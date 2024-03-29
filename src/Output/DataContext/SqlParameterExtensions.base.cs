using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Reflection;
using Source.DataContext.Outputs;

namespace Source.DataContext
{
    public static class SqlParameterExtensions
    {
        public static IEnumerable<SqlDataRecord> ToSqlParamCollection<T>(this T value)
        {
            var collection = new List<SqlDataRecord>();

            var list = value as IEnumerable;
            // scalar value is allowed
            if (list == null && value != null)
            {
                // create single row of type<T>
                list = new List<T> { value };
            }

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

                    if (sqlType == SqlDbType.NVarChar || sqlType == SqlDbType.VarBinary)
                    {
                        var maxLengthAttribute = (MaxLengthAttribute)property.GetCustomAttributes(typeof(MaxLengthAttribute), false).FirstOrDefault();
                        if (maxLengthAttribute != null)
                        {
                            metas.Add(new SqlMetaData(propName, sqlType, maxLengthAttribute?.Length ?? 0));
                        }
                        else
                        {
                            metas.Add(new SqlMetaData(propName, sqlType, SqlMetaData.Max));
                        }
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

        public static TOutput ToOutput<TOutput>(this IEnumerable<SqlParameter> parameters) where TOutput : class, IOutput, new()
        {
            var result = new TOutput();

            var outputs = parameters.ToList().Where(p => p.Direction == ParameterDirection.Output || p.Direction == ParameterDirection.InputOutput);

            var resultType = result.GetType();
            var properties = resultType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            var metas = new List<SqlMetaData>();
            var values = new List<object>();
            foreach (var output in outputs)
            {
                var parameterName = output.ParameterName.Replace("@", "");
                var property = properties.FirstOrDefault(p => p.Name.Equals(parameterName));
                if (property == null || output.Value == DBNull.Value)
                {
                    continue;
                }

                property.SetValue(result, output.Value);
            }

            // try to add recordId from context
            try
            {
                var contextParameter = parameters.FirstOrDefault(p => p.ParameterName.Replace("@", "").Equals("Context"));
                if (contextParameter != null && result.RecordId == null)
                {
                    var contextRecord = (contextParameter.Value as List<SqlDataRecord>)?.FirstOrDefault();
                    var recordIdColumn = contextRecord.GetOrdinal("RecordId");
                    var recordId = contextRecord?.GetValue(recordIdColumn);
                    if (recordId != DBNull.Value)
                    {
                        var recordIdProperty = properties.FirstOrDefault(p => p.Name.Equals("RecordId"));
                        recordIdProperty.SetValue(result, recordId);
                    }
                }
            }
            catch { }

            return result;
        }
    }
}

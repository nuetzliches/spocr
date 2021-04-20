using System;
using System.Data;

namespace SpocR.DataContext
{
    internal static class SqlDbHelper
    {
        public static Type GetType(SqlDbType sqlType, bool isNullable)
        {
            switch (sqlType)
            {
                case SqlDbType.BigInt:
                    return isNullable ? typeof(long?) : typeof(long);

                case SqlDbType.Timestamp:
                case SqlDbType.VarBinary:
                    return typeof(byte[]);

                case SqlDbType.Bit:
                    return isNullable ? typeof(bool?) : typeof(bool);

                case SqlDbType.Char:
                case SqlDbType.VarChar:
                case SqlDbType.NVarChar:
                    return typeof(string);

                case SqlDbType.DateTime:
                case SqlDbType.DateTime2:
                    return isNullable ? typeof(DateTime?) : typeof(DateTime);

                case SqlDbType.Decimal:
                case SqlDbType.Money:
                case SqlDbType.SmallMoney:
                    return isNullable ? typeof(decimal?) : typeof(decimal);

                case SqlDbType.Float:
                    return isNullable ? typeof(double?) : typeof(double);

                case SqlDbType.Int:
                    return isNullable ? typeof(int?) : typeof(int);

                case SqlDbType.UniqueIdentifier:
                    return isNullable ? typeof(Guid?) : typeof(Guid);

                case SqlDbType.SmallInt:
                    return isNullable ? typeof(short?) : typeof(short);

                case SqlDbType.TinyInt:
                    return isNullable ? typeof(byte?) : typeof(byte);

                case SqlDbType.DateTimeOffset:
                    return isNullable ? typeof(DateTimeOffset?) : typeof(DateTimeOffset);

                case SqlDbType.Variant:
                    return typeof(object);
                default:
                    // https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings
                    throw new ArgumentOutOfRangeException($"{nameof(SqlDbHelper)}.{nameof(GetType)} - SqlDbType {sqlType} not defined!");
            }
        }
    }
}
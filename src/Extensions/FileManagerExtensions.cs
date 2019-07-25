using System.Linq;
using System.Reflection;

namespace SpocR.Extensions
{
    public static class FileManagerExtensions
    {
        public static T OverwriteWith<T>(this T target, T source) where T : class
        {
            var t = typeof(T);
            var properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                var value = prop.GetValue(source, null);
                if (value != null)
                    prop.SetValue(target, value, null);
            }
            return target;
        }
    }

}
// using System.Collections;
// using System.Reflection;

// namespace SpocR.Serialization
// {
//     public class SerializeContractResolver : DefaultContractResolver
//     {
//         public static readonly SerializeContractResolver Instance = new SerializeContractResolver();

//         protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
//         {
//             var property = base.CreateProperty(member, memberSerialization);

//             if (property.PropertyType != typeof(string) &&
//                 typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
//             {
//                 property.ShouldSerialize = instance =>
//                 {
//                     var enumerable = default(IEnumerable);

//                     switch (member.MemberType)
//                     {
//                         case MemberTypes.Property:
//                             enumerable = instance
//                                 .GetType()
//                                 .GetProperty(member.Name)
//                                 .GetValue(instance, null) as IEnumerable;
//                             break;
//                         case MemberTypes.Field:
//                             enumerable = instance
//                                 .GetType()
//                                 .GetField(member.Name)
//                                 .GetValue(instance) as IEnumerable;
//                             break;
//                         default:
//                             break;

//                     }

//                     return enumerable != null ? enumerable.GetEnumerator().MoveNext() : true;
//                 };
//             }
//             return property;
//         }
//     }
// }
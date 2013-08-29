using System;
using System.Reflection;

namespace FritzBoxApi.Extensions {
    public static class MemberInfoCustomAttributeExtension {
        public static T GetCustomAttribute<T>(this MemberInfo element) where T : Attribute {
            return (T)Attribute.GetCustomAttribute(element, typeof(T));
        }
    }
}
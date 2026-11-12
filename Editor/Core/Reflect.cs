using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TurboOptimizer
{
    // Все сторонние пакеты (VRChat SDK, AAO, Meshia, d4rk) вызываются через рефлексию,
    // чтобы плагин компилировался даже если какой-то пакет отсутствует или поменял версию.
    internal static class Reflect
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static readonly Dictionary<string, Type> Cache = new Dictionary<string, Type>();

        public static Type FindType(string fullName)
        {
            if (Cache.TryGetValue(fullName, out var t)) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = a.GetType(fullName, false);
                if (t != null) break;
            }
            Cache[fullName] = t;
            return t;
        }

        public static Type FindTypeByName(string name, Func<Type, bool> filter = null)
        {
            var key = "name:" + name;
            if (Cache.TryGetValue(key, out var t)) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
                t = types.FirstOrDefault(x => x.Name == name && (filter == null || filter(x)));
                if (t != null) break;
            }
            Cache[key] = t;
            return t;
        }

        // Чтение поля/свойства по пути "physBone.componentCount", без учёта регистра.
        public static object GetPath(object obj, string path)
        {
            foreach (var part in path.Split('.'))
            {
                if (obj == null) return null;
                var type = obj.GetType();
                var f = type.GetFields(Any).FirstOrDefault(x => string.Equals(x.Name, part, StringComparison.OrdinalIgnoreCase));
                if (f != null) { obj = f.GetValue(obj); continue; }
                var p = type.GetProperties(Any).FirstOrDefault(x => x.GetIndexParameters().Length == 0 && string.Equals(x.Name, part, StringComparison.OrdinalIgnoreCase));
                if (p != null) { obj = p.GetValue(obj); continue; }
                return null;
            }
            return obj;
        }

        public static void Set(object obj, string name, object value)
        {
            var type = obj.GetType();
            var p = type.GetProperty(name, Any);
            if (p != null && p.CanWrite) { p.SetValue(obj, value); return; }
            var f = type.GetField(name, Any);
            f?.SetValue(obj, value);
        }

        public static object Call(object obj, string method, params object[] args)
        {
            var m = obj.GetType().GetMethods(Any).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            return m?.Invoke(obj, args);
        }

        public static double? ToNumber(object v)
        {
            if (v == null) return null;
            if (v is bool b) return b ? 1 : 0;
            if (v is IConvertible) { try { return Convert.ToDouble(v); } catch { return null; } }
            return null;
        }
    }
}

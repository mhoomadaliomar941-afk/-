using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace OrganizationEmpire.Core.Json
{
    /// <summary>Excludes a property or field from JSON (de)serialization.</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class JsonIgnoreAttribute : Attribute { }

    /// <summary>
    /// Reflection based POCO &lt;-&gt; JSON tree mapper.
    /// Supports public read/write properties and public non-readonly fields, primitives, enums (as names),
    /// DateTime, Guid, Nullable, List/arrays/HashSet and Dictionary with string or enum keys.
    /// Unknown JSON members are ignored and missing ones keep their default value, which makes
    /// save files forward and backward compatible.
    /// </summary>
    public static class JsonMapper
    {
        private sealed class MemberAccessor
        {
            public string Name;
            public Type Type;
            public Func<object, object> Get;
            public Action<object, object> Set;
        }

        private static readonly Dictionary<Type, List<MemberAccessor>> Cache = new Dictionary<Type, List<MemberAccessor>>();
        private static readonly object CacheLock = new object();

        public static string Serialize(object value, bool indented = true)
        {
            return Json.Stringify(ToTree(value), indented);
        }

        public static T Deserialize<T>(string json)
        {
            return (T)FromTree(Json.Parse(json), typeof(T));
        }

        public static T Clone<T>(T value)
        {
            return (T)FromTree(ToTree(value), typeof(T));
        }

        public static object ToTree(object value)
        {
            return ToTree(value, 0);
        }

        private static object ToTree(object value, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("Object graph too deep (cycle?)");
            if (value == null) return null;
            Type t = value.GetType();

            if (value is string s) return s;
            if (value is bool b) return b;
            if (t.IsEnum) return value.ToString();
            if (value is float f) return (double)f;
            if (value is double d) return d;
            if (value is decimal m) return (double)m;
            if (IsIntegral(t)) return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (value is DateTime dt) return dt.ToString("o", CultureInfo.InvariantCulture);
            if (value is Guid g) return g.ToString();
            if (value is TimeSpan ts) return ts.TotalMilliseconds;

            if (value is IDictionary dict)
            {
                var obj = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry e in dict)
                {
                    string key = Convert.ToString(e.Key, CultureInfo.InvariantCulture);
                    obj[key] = ToTree(e.Value, depth + 1);
                }
                return obj;
            }

            if (value is IEnumerable en)
            {
                var arr = new List<object>();
                foreach (object item in en) arr.Add(ToTree(item, depth + 1));
                return arr;
            }

            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var acc in GetMembers(t))
            {
                result[acc.Name] = ToTree(acc.Get(value), depth + 1);
            }
            return result;
        }

        public static object FromTree(object node, Type type)
        {
            return FromTree(node, type, 0);
        }

        private static object FromTree(object node, Type type, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("JSON too deep");

            Type underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                if (node == null) return null;
                return FromTree(node, underlying, depth);
            }

            if (node == null)
            {
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }

            if (type == typeof(object)) return node;
            if (type == typeof(string)) return node is string str ? str : Convert.ToString(node, CultureInfo.InvariantCulture);
            if (type == typeof(bool))
            {
                if (node is bool bb) return bb;
                if (node is string bs) return bs.Equals("true", StringComparison.OrdinalIgnoreCase) || bs == "1";
                if (node is long bl) return bl != 0;
                return false;
            }
            if (type.IsEnum) return ParseEnum(node, type);
            if (type == typeof(DateTime))
            {
                if (node is string ds && DateTime.TryParse(ds, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                    return parsed;
                return default(DateTime);
            }
            if (type == typeof(Guid))
            {
                return node is string gs && Guid.TryParse(gs, out Guid guid) ? guid : Guid.Empty;
            }
            if (type == typeof(TimeSpan))
            {
                return TimeSpan.FromMilliseconds(ToDouble(node));
            }
            if (type == typeof(double)) return ToDouble(node);
            if (type == typeof(float)) return (float)ToDouble(node);
            if (type == typeof(decimal)) return (decimal)ToDouble(node);
            if (IsIntegral(type))
            {
                double dv = ToDouble(node);
                try
                {
                    return Convert.ChangeType(Math.Round(dv), type, CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    return Activator.CreateInstance(type);
                }
            }

            if (type.IsArray)
            {
                Type elem = type.GetElementType();
                var list = node as List<object> ?? new List<object>();
                Array arr = Array.CreateInstance(elem, list.Count);
                for (int i = 0; i < list.Count; i++) arr.SetValue(FromTree(list[i], elem, depth + 1), i);
                return arr;
            }

            if (type.IsGenericType)
            {
                Type def = type.GetGenericTypeDefinition();
                Type[] args = type.GetGenericArguments();
                if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(ICollection<>) || def == typeof(IReadOnlyList<>))
                {
                    var listType = typeof(List<>).MakeGenericType(args[0]);
                    var list = (IList)Activator.CreateInstance(listType);
                    if (node is List<object> src)
                    {
                        foreach (object item in src) list.Add(FromTree(item, args[0], depth + 1));
                    }
                    return list;
                }
                if (def == typeof(HashSet<>))
                {
                    object set = Activator.CreateInstance(type);
                    MethodInfo add = type.GetMethod("Add");
                    if (node is List<object> src)
                    {
                        foreach (object item in src) add.Invoke(set, new[] { FromTree(item, args[0], depth + 1) });
                    }
                    return set;
                }
                if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>))
                {
                    var dictType = typeof(Dictionary<,>).MakeGenericType(args[0], args[1]);
                    var dict = (IDictionary)Activator.CreateInstance(dictType);
                    if (node is Dictionary<string, object> src)
                    {
                        foreach (var kv in src)
                        {
                            object key;
                            if (args[0] == typeof(string)) key = kv.Key;
                            else if (args[0].IsEnum)
                            {
                                key = ParseEnum(kv.Key, args[0]);
                            }
                            else key = Convert.ChangeType(kv.Key, args[0], CultureInfo.InvariantCulture);
                            dict[key] = FromTree(kv.Value, args[1], depth + 1);
                        }
                    }
                    return dict;
                }
            }

            // Plain object
            var objNode = node as Dictionary<string, object>;
            object instance;
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (MissingMethodException)
            {
                throw new InvalidOperationException("Type " + type.FullName + " needs a public parameterless constructor for JSON mapping.");
            }
            if (objNode == null) return instance;
            foreach (var acc in GetMembers(type))
            {
                if (!objNode.TryGetValue(acc.Name, out object child)) continue;
                try
                {
                    acc.Set(instance, FromTree(child, acc.Type, depth + 1));
                }
                catch (Exception ex) when (!(ex is InvalidOperationException))
                {
                    // Tolerate a single malformed member rather than failing the whole load.
                }
            }
            return instance;
        }

        private static object ParseEnum(object node, Type enumType)
        {
            if (node is string s)
            {
                try
                {
                    return Enum.Parse(enumType, s, true);
                }
                catch (ArgumentException)
                {
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ln))
                        return Enum.ToObject(enumType, ln);
                    return Activator.CreateInstance(enumType);
                }
            }
            if (node is long l) return Enum.ToObject(enumType, l);
            if (node is double d) return Enum.ToObject(enumType, (long)d);
            return Activator.CreateInstance(enumType);
        }

        private static double ToDouble(object node)
        {
            switch (node)
            {
                case long l: return l;
                case double d: return d;
                case bool b: return b ? 1 : 0;
                case string s:
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0;
                default: return 0;
            }
        }

        private static bool IsIntegral(Type t)
        {
            return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);
        }

        private static List<MemberAccessor> GetMembers(Type t)
        {
            lock (CacheLock)
            {
                if (Cache.TryGetValue(t, out var cached)) return cached;
                var list = new List<MemberAccessor>();
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                    if (p.GetSetMethod() == null || p.GetGetMethod() == null) continue;
                    if (p.IsDefined(typeof(JsonIgnoreAttribute), true)) continue;
                    var prop = p;
                    list.Add(new MemberAccessor
                    {
                        Name = prop.Name,
                        Type = prop.PropertyType,
                        Get = o => prop.GetValue(o, null),
                        Set = (o, v) => prop.SetValue(o, v, null),
                    });
                }
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    if (f.IsDefined(typeof(JsonIgnoreAttribute), true)) continue;
                    var field = f;
                    list.Add(new MemberAccessor
                    {
                        Name = field.Name,
                        Type = field.FieldType,
                        Get = o => field.GetValue(o),
                        Set = (o, v) => field.SetValue(o, v),
                    });
                }
                Cache[t] = list;
                return list;
            }
        }
    }
}

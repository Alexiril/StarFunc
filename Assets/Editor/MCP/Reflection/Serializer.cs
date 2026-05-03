using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MCP.Reflection
{
    public static class Serializer
    {
        public static JToken Serialize(object value, int maxDepth = 4)
        {
            return SerializeInternal(value, maxDepth, new HashSet<object>(ReferenceEqualityComparer.Default));
        }

        static JToken SerializeInternal(object value, int depth, HashSet<object> seen)
        {
            if (value == null) return JValue.CreateNull();

            try
            {
                if (value is Object uo)
                {
                    if (uo == null) return JValue.CreateNull();
                    return ObjectRef.ToRef(uo);
                }
            }
            catch (MissingReferenceException) { return new JObject { ["type"] = "Missing", ["broken"] = true }; }

            var type = value.GetType();

            if (value is string s) return new JValue(s);
            if (type.IsPrimitive || value is decimal) return JToken.FromObject(value);
            if (value is Enum) return new JObject { ["__enum"] = value.ToString(), ["value"] = Convert.ToInt64(value) };

            switch (value)
            {
                case Vector2 v2: return new JObject { ["x"] = v2.x, ["y"] = v2.y };
                case Vector3 v3: return new JObject { ["x"] = v3.x, ["y"] = v3.y, ["z"] = v3.z };
                case Vector4 v4: return new JObject { ["x"] = v4.x, ["y"] = v4.y, ["z"] = v4.z, ["w"] = v4.w };
                case Vector2Int vi2: return new JObject { ["x"] = vi2.x, ["y"] = vi2.y };
                case Vector3Int vi3: return new JObject { ["x"] = vi3.x, ["y"] = vi3.y, ["z"] = vi3.z };
                case Quaternion q: return new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w, ["eulerAngles"] = SerializeInternal(q.eulerAngles, depth - 1, seen) };
                case Color c: return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };
                case Color32 c32: return new JObject { ["r"] = c32.r, ["g"] = c32.g, ["b"] = c32.b, ["a"] = c32.a };
                case Rect r: return new JObject { ["x"] = r.x, ["y"] = r.y, ["width"] = r.width, ["height"] = r.height };
                case Bounds b: return new JObject { ["center"] = SerializeInternal(b.center, depth - 1, seen), ["size"] = SerializeInternal(b.size, depth - 1, seen) };
                case LayerMask lm: return new JObject { ["mask"] = lm.value };
            }

            if (depth <= 0) return new JValue(value.ToString());

            if (!type.IsValueType)
            {
                if (!seen.Add(value)) return new JValue("<cycle>");
            }

            try
            {
                if (value is IDictionary dict)
                {
                    var jo = new JObject();
                    foreach (DictionaryEntry e in dict)
                        jo[e.Key?.ToString() ?? ""] = SerializeInternal(e.Value, depth - 1, seen);
                    return jo;
                }

                if (value is IEnumerable en && !(value is string))
                {
                    var arr = new JArray();
                    foreach (var item in en)
                        arr.Add(SerializeInternal(item, depth - 1, seen));
                    return arr;
                }

                var members = new JObject();
                foreach (var f in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    try { members[f.Name] = SerializeInternal(f.GetValue(value), depth - 1, seen); }
                    catch (Exception e) { members[f.Name] = "<error: " + e.Message + ">"; }
                }
                return members;
            }
            finally
            {
                if (!type.IsValueType) seen.Remove(value);
            }
        }

        class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Default = new ReferenceEqualityComparer();
            bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}

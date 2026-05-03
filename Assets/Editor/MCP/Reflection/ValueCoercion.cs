using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MCP.Reflection
{
    public static class ValueCoercion
    {
        public static object Coerce(JToken token, Type targetType)
        {
            if (token == null || token.Type == JTokenType.Null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            // {"__type": "FullName"} envelope → System.Type. Use this to pass typeof(X) into APIs
            // like Resources.Load(string, Type) or AssetDatabase.LoadAssetAtPath(string, Type).
            if (token is JObject typeObj && typeObj["__type"] != null && targetType == typeof(Type))
            {
                var name = (string)typeObj["__type"];
                if (string.IsNullOrEmpty(name)) throw new ArgumentException("__type envelope is empty");
                var matches = TypeUtil.ResolveAll(name);
                if (matches.Count == 0) throw new ArgumentException($"__type not resolved: {name}");
                if (matches.Count > 1) throw new ArgumentException($"__type ambiguous '{name}'. Use the AssemblyQualifiedName.");
                return matches[0];
            }

            if (typeof(Object).IsAssignableFrom(targetType))
                return ObjectRef.ResolveStrict(token);

            if (token is JObject jo && jo[ObjectRef.RefMarker] != null)
                return ObjectRef.ResolveStrict(jo);

            if (targetType == typeof(string)) return (string)token;
            if (targetType == typeof(bool)) return (bool)token;
            if (targetType == typeof(int)) return (int)token;
            if (targetType == typeof(long)) return (long)token;
            if (targetType == typeof(float)) return (float)token;
            if (targetType == typeof(double)) return (double)token;
            if (targetType == typeof(byte)) return (byte)token;
            if (targetType == typeof(short)) return (short)token;
            if (targetType == typeof(uint)) return (uint)token;
            if (targetType == typeof(ulong)) return (ulong)token;

            if (targetType.IsEnum)
            {
                if (token.Type == JTokenType.Integer) return Enum.ToObject(targetType, (long)token);
                if (token.Type == JTokenType.String) return Enum.Parse(targetType, (string)token, ignoreCase: true);
                if (token is JObject ej && ej["value"] != null) return Enum.ToObject(targetType, (long)ej["value"]);
                throw new ArgumentException($"Cannot coerce {token.Type} to enum {targetType.Name}");
            }

            if (targetType == typeof(Vector2)) return new Vector2(F(token, "x"), F(token, "y"));
            if (targetType == typeof(Vector3)) return new Vector3(F(token, "x"), F(token, "y"), F(token, "z"));
            if (targetType == typeof(Vector4)) return new Vector4(F(token, "x"), F(token, "y"), F(token, "z"), F(token, "w"));
            if (targetType == typeof(Vector2Int)) return new Vector2Int(I(token, "x"), I(token, "y"));
            if (targetType == typeof(Vector3Int)) return new Vector3Int(I(token, "x"), I(token, "y"), I(token, "z"));
            if (targetType == typeof(Quaternion))
            {
                if (token["eulerAngles"] is JObject ea)
                    return Quaternion.Euler(F(ea, "x"), F(ea, "y"), F(ea, "z"));
                return new Quaternion(F(token, "x"), F(token, "y"), F(token, "z"), F(token, "w"));
            }
            if (targetType == typeof(Color)) return new Color(F(token, "r"), F(token, "g"), F(token, "b"), token["a"] != null ? F(token, "a") : 1f);
            if (targetType == typeof(Color32)) return new Color32((byte)I(token, "r"), (byte)I(token, "g"), (byte)I(token, "b"), token["a"] != null ? (byte)I(token, "a") : (byte)255);
            if (targetType == typeof(Rect)) return new Rect(F(token, "x"), F(token, "y"), F(token, "width"), F(token, "height"));
            if (targetType == typeof(Bounds))
                return new Bounds(
                    (Vector3)Coerce(token["center"], typeof(Vector3)),
                    (Vector3)Coerce(token["size"], typeof(Vector3)));
            if (targetType == typeof(LayerMask))
            {
                if (token.Type == JTokenType.Integer) return (LayerMask)(int)token;
                if (token is JObject lj && lj["mask"] != null) return (LayerMask)(int)lj["mask"];
                if (token is JArray la)
                {
                    int mask = 0;
                    foreach (var n in la)
                    {
                        var layer = LayerMask.NameToLayer((string)n);
                        if (layer >= 0) mask |= 1 << layer;
                    }
                    return (LayerMask)mask;
                }
                throw new ArgumentException("Cannot coerce to LayerMask");
            }

            if (targetType.IsArray)
            {
                var elemType = targetType.GetElementType();
                var arr = (JArray)token;
                var result = Array.CreateInstance(elemType, arr.Count);
                for (int i = 0; i < arr.Count; i++) result.SetValue(Coerce(arr[i], elemType), i);
                return result;
            }

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elemType = targetType.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(targetType);
                foreach (var t in (JArray)token) list.Add(Coerce(t, elemType));
                return list;
            }

            // Walk fields manually for structs and plain (non-Object, non-collection) classes.
            // Without this, JToken.ToObject hands the JObject to Newtonsoft, which has no concept
            // of {__ref:true,...} envelopes — it tries to construct nested UnityEngine.Object fields
            // via `new T()`, silently writing nulls (and emitting "must be instantiated using
            // ScriptableObject.CreateInstance" warnings for SO subtypes). Recursive Coerce here lets
            // nested ObjectRef envelopes resolve correctly.
            if (token is JObject genericObj
                && !targetType.IsPrimitive
                && !targetType.IsEnum
                && targetType != typeof(string)
                && !typeof(Object).IsAssignableFrom(targetType)
                && !typeof(IEnumerable).IsAssignableFrom(targetType))
            {
                object instance;
                try { instance = Activator.CreateInstance(targetType); }
                catch { return token.ToObject(targetType); }

                const BindingFlags fieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                for (var t = targetType; t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(fieldFlags | BindingFlags.DeclaredOnly))
                    {
                        bool serializable = f.IsPublic || f.IsDefined(typeof(SerializeField), inherit: true);
                        if (!serializable) continue;
                        if (genericObj[f.Name] == null) continue;
                        var coerced = Coerce(genericObj[f.Name], f.FieldType);
                        f.SetValue(instance, coerced);
                    }
                }
                return instance;
            }

            return token.ToObject(targetType);
        }

        static float F(JToken t, string k) => t[k] != null ? (float)t[k] : 0f;
        static int I(JToken t, string k) => t[k] != null ? (int)t[k] : 0;
    }
}

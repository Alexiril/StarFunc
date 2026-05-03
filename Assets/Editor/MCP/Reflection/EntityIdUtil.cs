using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCP.Reflection
{
    public static class EntityIdUtil
    {
        public static long ToWire(Object obj) => unchecked((long)EntityId.ToULong(obj.GetEntityId()));
        public static EntityId FromWire(long wire) => EntityId.FromULong(unchecked((ulong)wire));

        public static long? ReadWire(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Integer) return (long)token;
            return null;
        }

        public static Object Resolve(long wire) => EditorUtility.EntityIdToObject(FromWire(wire));
    }
}

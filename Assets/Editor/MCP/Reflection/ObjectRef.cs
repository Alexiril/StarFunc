using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCP.Reflection
{
    public static class ObjectRef
    {
        public const string RefMarker = "__ref";

        public static JObject ToRef(Object obj)
        {
            if (obj == null) return null;

            var jo = new JObject
            {
                [RefMarker] = true,
                ["instanceId"] = EntityIdUtil.ToWire(obj),
                ["name"] = obj.name,
                ["type"] = obj.GetType().FullName
            };

            try
            {
                var gid = GlobalObjectId.GetGlobalObjectIdSlow(obj);
                jo["globalObjectId"] = gid.ToString();
            }
            catch { }

            if (obj is GameObject go)
            {
                jo["path"] = GetHierarchyPath(go.transform);
                jo["sceneName"] = go.scene.name;
            }
            else if (obj is Component cmp && cmp != null)
            {
                jo["path"] = GetHierarchyPath(cmp.transform);
                jo["sceneName"] = cmp.gameObject.scene.name;
                jo["gameObjectInstanceId"] = EntityIdUtil.ToWire(cmp.gameObject);
            }
            else
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    jo["assetPath"] = path;
                    jo["guid"] = AssetDatabase.AssetPathToGUID(path);
                }
            }

            return jo;
        }

        public static Object Resolve(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token is JValue v && v.Type == JTokenType.Integer)
                return EntityIdUtil.Resolve(v.Value<long>());

            if (!(token is JObject jo)) return null;

            var gidStr = (string)jo["globalObjectId"];
            if (!string.IsNullOrEmpty(gidStr) && GlobalObjectId.TryParse(gidStr, out var gid))
            {
                var resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                if (resolved != null) return resolved;
            }

            if (jo["instanceId"] != null && jo["instanceId"].Type == JTokenType.Integer)
            {
                var byId = EntityIdUtil.Resolve((long)jo["instanceId"]);
                if (byId != null) return byId;
            }

            var path = (string)jo["path"];
            var sceneName = (string)jo["sceneName"];
            if (!string.IsNullOrEmpty(path))
            {
                var go = FindByPath(path, sceneName);
                if (go != null)
                {
                    var typeName = (string)jo["type"];
                    if (!string.IsNullOrEmpty(typeName) && typeName != typeof(GameObject).FullName)
                    {
                        var t = TypeUtil.Resolve(typeName);
                        if (t != null && typeof(Component).IsAssignableFrom(t))
                            return go.GetComponent(t);
                    }
                    return go;
                }
            }

            var assetPath = (string)jo["assetPath"];
            if (!string.IsNullOrEmpty(assetPath))
            {
                var typeName = (string)jo["type"];
                if (!string.IsNullOrEmpty(typeName))
                {
                    var t = TypeUtil.Resolve(typeName);
                    if (t != null)
                    {
                        var byPath = AssetDatabase.LoadAssetAtPath(assetPath, t);
                        if (byPath != null) return byPath;
                    }
                }
                var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (main != null) return main;
            }

            var guid = (string)jo["guid"];
            if (!string.IsNullOrEmpty(guid))
            {
                var byGuidPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(byGuidPath))
                {
                    var typeName = (string)jo["type"];
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var t = TypeUtil.Resolve(typeName);
                        if (t != null) return AssetDatabase.LoadAssetAtPath(byGuidPath, t);
                    }
                    return AssetDatabase.LoadMainAssetAtPath(byGuidPath);
                }
            }

            return null;
        }

        // Like Resolve, but throws on resolution failure when the input was a non-null token.
        // Use this anywhere a null result would silently corrupt data (e.g. setting an Object
        // reference field) — the throw surfaces stale/invalid refs instead of writing nulls.
        public static Object ResolveStrict(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            var resolved = Resolve(token);
            if (resolved != null) return resolved;

            var summary = "(non-object token)";
            if (token is JObject jo)
            {
                var keys = new System.Collections.Generic.List<string>();
                foreach (var p in jo.Properties()) keys.Add(p.Name);
                summary = "{" + string.Join(", ", keys) + "}";
            }
            throw new System.ArgumentException(
                $"ObjectRef did not resolve. Tried (in order): globalObjectId, instanceId, path+sceneName, assetPath, guid. Provided fields: {summary}. " +
                "Likely causes: stale instanceId after domain reload, mistyped path, asset moved/deleted, or unsupported envelope shape.");
        }

        public static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "";
            var sb = new System.Text.StringBuilder();
            var stack = new System.Collections.Generic.Stack<string>();
            for (var cur = t; cur != null; cur = cur.parent) stack.Push(cur.name);
            while (stack.Count > 0)
            {
                sb.Append('/').Append(stack.Pop());
            }
            return sb.ToString();
        }

        static GameObject FindByPath(string path, string sceneName)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = path.TrimStart('/').Split('/');
            if (parts.Length == 0) return null;

            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                if (!string.IsNullOrEmpty(sceneName) && scene.name != sceneName) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != parts[0]) continue;
                    var cur = root.transform;
                    bool ok = true;
                    for (int p = 1; p < parts.Length && ok; p++)
                    {
                        var child = cur.Find(parts[p]);
                        if (child == null) { ok = false; break; }
                        cur = child;
                    }
                    if (ok) return cur.gameObject;
                }
            }
            return null;
        }
    }
}

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MCP.Protocol;
using MCP.Reflection;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class HierarchyTools
    {
        static HierarchyTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "hierarchy_get",
                Description = "Get the GameObject hierarchy of currently loaded scene(s).",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["sceneName"] = new JObject { ["type"] = "string" },
                        ["rootInstanceId"] = new JObject { ["type"] = "integer" },
                        ["maxDepth"] = new JObject { ["type"] = "integer", ["default"] = 32 },
                        ["includeInactive"] = new JObject { ["type"] = "boolean", ["default"] = true }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => GetHierarchy(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "gameobject_create",
                Description = "Create a new GameObject. Optional parent ref, scene name, position/rotation/eulerAngles/localScale.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "name" },
                    ["properties"] = new JObject
                    {
                        ["name"] = new JObject { ["type"] = "string" },
                        ["parent"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of parent GameObject" },
                        ["sceneName"] = new JObject { ["type"] = "string" },
                        ["position"] = new JObject { ["type"] = "object" },
                        ["rotation"] = new JObject { ["type"] = "object" },
                        ["eulerAngles"] = new JObject { ["type"] = "object" },
                        ["localScale"] = new JObject { ["type"] = "object" },
                        ["isLocalSpace"] = new JObject { ["type"] = "boolean", ["default"] = true }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => CreateGameObject(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "gameobject_update",
                Description = "Update an existing GameObject: rename, set active, reparent (pass null to unparent), move to scene, update transform, set sibling index.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "gameObject" },
                    ["properties"] = new JObject
                    {
                        ["gameObject"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of the GameObject to update" },
                        ["name"] = new JObject { ["type"] = "string" },
                        ["active"] = new JObject { ["type"] = "boolean" },
                        ["parent"] = new JObject { ["description"] = "ObjectRef of new parent, or null to unparent" },
                        ["sceneName"] = new JObject { ["type"] = "string", ["description"] = "Move to this loaded scene (top-level only)" },
                        ["position"] = new JObject { ["type"] = "object" },
                        ["rotation"] = new JObject { ["type"] = "object" },
                        ["eulerAngles"] = new JObject { ["type"] = "object" },
                        ["localScale"] = new JObject { ["type"] = "object" },
                        ["siblingIndex"] = new JObject { ["type"] = "integer" },
                        ["isLocalSpace"] = new JObject { ["type"] = "boolean", ["default"] = true }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => UpdateGameObject(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "gameobject_destroy",
                Description = "Destroy a GameObject (and all its children/components). Supports Undo.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "gameObject" },
                    ["properties"] = new JObject
                    {
                        ["gameObject"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of the GameObject to destroy" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => DestroyGameObject(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "gameobjects_find",
                Description = "Find GameObjects in loaded scenes by name (regex), tag, layer, and/or component type. All filters are AND-combined; omit a filter to skip it. Returns flat array of ObjectRefs (no children) — use hierarchy_get for tree traversal.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["namePattern"] = new JObject { ["type"] = "string", ["description"] = "Case-insensitive regex matched against GameObject.name." },
                        ["tag"] = new JObject { ["type"] = "string" },
                        ["layer"] = new JObject { ["description"] = "Layer index (int) or layer name (string)." },
                        ["componentTypeName"] = new JObject { ["type"] = "string", ["description"] = "Full or simple Component type name; only GameObjects with at least one matching component are returned." },
                        ["sceneName"] = new JObject { ["type"] = "string", ["description"] = "Limit search to one loaded scene." },
                        ["includeInactive"] = new JObject { ["type"] = "boolean", ["default"] = true },
                        ["limit"] = new JObject { ["type"] = "integer", ["default"] = 1000 }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => FindGameObjects(args))
            });
        }

        static JToken FindGameObjects(JObject args)
        {
            bool includeInactive = args["includeInactive"] == null || (bool)args["includeInactive"];
            int limit = args["limit"] != null ? (int)args["limit"] : 1000;
            var sceneName = (string)args["sceneName"];
            var tag = (string)args["tag"];
            var namePattern = (string)args["namePattern"];

            int? layerIndex = null;
            if (args["layer"] != null && args["layer"].Type != JTokenType.Null)
            {
                if (args["layer"].Type == JTokenType.Integer)
                    layerIndex = (int)args["layer"];
                else if (args["layer"].Type == JTokenType.String)
                {
                    var name = (string)args["layer"];
                    var idx = LayerMask.NameToLayer(name);
                    if (idx < 0) throw new ArgumentException($"Unknown layer name: {name}");
                    layerIndex = idx;
                }
                else throw new ArgumentException("layer must be int or string");
            }

            Type componentType = null;
            var componentTypeName = (string)args["componentTypeName"];
            if (!string.IsNullOrEmpty(componentTypeName))
            {
                var matches = TypeUtil.ResolveAll(componentTypeName);
                matches.RemoveAll(t => !typeof(Component).IsAssignableFrom(t));
                if (matches.Count == 0) throw new ArgumentException($"No Component type matches '{componentTypeName}'");
                if (matches.Count > 1)
                {
                    var names = new JArray();
                    foreach (var t in matches) names.Add(t.AssemblyQualifiedName);
                    throw new ArgumentException($"Ambiguous componentTypeName '{componentTypeName}'. Candidates: {string.Join(", ", names)}");
                }
                componentType = matches[0];
            }

            Regex regex = null;
            if (!string.IsNullOrEmpty(namePattern))
            {
                try { regex = new Regex(namePattern, RegexOptions.IgnoreCase); }
                catch (ArgumentException e) { throw new ArgumentException("Invalid namePattern regex: " + e.Message); }
            }

            var pool = UnityEngine.Object.FindObjectsByType<GameObject>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);

            var results = new JArray();
            int count = 0;
            int totalMatches = 0;
            foreach (var go in pool)
            {
                if (!string.IsNullOrEmpty(sceneName) && go.scene.name != sceneName) continue;
                if (!string.IsNullOrEmpty(tag) && !go.CompareTag(tag)) continue;
                if (layerIndex.HasValue && go.layer != layerIndex.Value) continue;
                if (regex != null && !regex.IsMatch(go.name)) continue;
                if (componentType != null && go.GetComponent(componentType) == null) continue;

                totalMatches++;
                if (count < limit)
                {
                    var node = ObjectRef.ToRef(go);
                    node["activeSelf"] = go.activeSelf;
                    node["activeInHierarchy"] = go.activeInHierarchy;
                    node["scene"] = go.scene.name;
                    results.Add(node);
                    count++;
                }
            }

            return new JObject
            {
                ["totalMatches"] = totalMatches,
                ["returned"] = count,
                ["entries"] = results
            };
        }

        static JToken GetHierarchy(JObject args)
        {
            int maxDepth = args["maxDepth"] != null ? (int)args["maxDepth"] : 32;
            bool includeInactive = args["includeInactive"] == null || (bool)args["includeInactive"];
            var sceneName = (string)args["sceneName"];

            if (args["rootInstanceId"] != null)
            {
                var rootId = (long)args["rootInstanceId"];
                var rootObj = MCP.Reflection.EntityIdUtil.Resolve(rootId) as GameObject;
                if (rootObj == null) throw new ArgumentException($"No GameObject with instanceId {rootId}");
                return SerializeNode(rootObj.transform, maxDepth, includeInactive);
            }

            var roots = new JArray();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                if (!s.IsValid() || !s.isLoaded) continue;
                if (!string.IsNullOrEmpty(sceneName) && s.name != sceneName) continue;
                var sceneObj = new JObject { ["name"] = s.name, ["path"] = s.path, ["roots"] = SerializeRoots(s, maxDepth, includeInactive) };
                roots.Add(sceneObj);
            }
            return new JObject { ["scenes"] = roots };
        }

        static JArray SerializeRoots(Scene s, int maxDepth, bool includeInactive)
        {
            var arr = new JArray();
            foreach (var root in s.GetRootGameObjects())
            {
                if (!includeInactive && !root.activeInHierarchy) continue;
                arr.Add(SerializeNode(root.transform, maxDepth, includeInactive));
            }
            return arr;
        }

        static JObject SerializeNode(Transform t, int depth, bool includeInactive)
        {
            var go = t.gameObject;
            var node = ObjectRef.ToRef(go);
            node["activeSelf"] = go.activeSelf;
            node["activeInHierarchy"] = go.activeInHierarchy;
            node["componentCount"] = go.GetComponents<Component>().Length;
            if (depth > 0 && t.childCount > 0)
            {
                var children = new JArray();
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (!includeInactive && !c.gameObject.activeInHierarchy) continue;
                    children.Add(SerializeNode(c, depth - 1, includeInactive));
                }
                node["children"] = children;
            }
            return node;
        }

        static JToken UpdateGameObject(JObject args)
        {
            var resolved = ObjectRef.Resolve(args["gameObject"]);
            var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
            if (go == null) throw new ArgumentException("gameObject does not resolve to a GameObject");

            Undo.SetCurrentGroupName($"MCP Update GameObject '{go.name}'");
            int undoGroup = Undo.GetCurrentGroup();

            if (args["name"] is JValue nameVal && nameVal.Type == JTokenType.String)
            {
                Undo.RecordObject(go, "Rename");
                go.name = (string)nameVal;
            }

            if (args["active"] is JValue activeVal && activeVal.Type == JTokenType.Boolean)
            {
                Undo.RecordObject(go, "Set Active");
                go.SetActive((bool)activeVal);
            }

            if (args.ContainsKey("parent"))
            {
                var parentToken = args["parent"];
                if (parentToken == null || parentToken.Type == JTokenType.Null)
                {
                    Undo.SetTransformParent(go.transform, null, "Unparent");
                }
                else
                {
                    var parentResolved = ObjectRef.Resolve(parentToken);
                    var parentGo = parentResolved as GameObject ?? (parentResolved as Component)?.gameObject;
                    if (parentGo == null) throw new ArgumentException("parent does not resolve to a GameObject");
                    if (!parentGo.scene.IsValid())
                        throw new ArgumentException("parent is a Prefab Asset (not a scene object). Use prefab_open to enter Prefab edit mode, or pass a scene GameObject.");
                    Undo.SetTransformParent(go.transform, parentGo.transform, "Reparent");
                }
            }

            if (args["sceneName"] is JValue sceneVal)
            {
                var scene = EditorSceneManager.GetSceneByName((string)sceneVal);
                if (!scene.IsValid()) throw new ArgumentException($"Scene '{(string)sceneVal}' not loaded");
                EditorSceneManager.MoveGameObjectToScene(go, scene);
            }

            bool local = args["isLocalSpace"] == null || (bool)args["isLocalSpace"];
            bool hasTransform = args["position"] != null || args["rotation"] != null ||
                                args["eulerAngles"] != null || args["localScale"] != null ||
                                args["siblingIndex"] != null;
            if (hasTransform)
            {
                Undo.RecordObject(go.transform, "Set Transform");
                if (args["position"] is JObject pos)
                {
                    var v = (Vector3)ValueCoercion.Coerce(pos, typeof(Vector3));
                    if (local) go.transform.localPosition = v; else go.transform.position = v;
                }
                if (args["rotation"] is JObject rot)
                {
                    var q = (Quaternion)ValueCoercion.Coerce(rot, typeof(Quaternion));
                    if (local) go.transform.localRotation = q; else go.transform.rotation = q;
                }
                else if (args["eulerAngles"] is JObject ea)
                {
                    var v = (Vector3)ValueCoercion.Coerce(ea, typeof(Vector3));
                    if (local) go.transform.localEulerAngles = v; else go.transform.eulerAngles = v;
                }
                if (args["localScale"] is JObject sc)
                    go.transform.localScale = (Vector3)ValueCoercion.Coerce(sc, typeof(Vector3));
                if (args["siblingIndex"] is JValue si)
                    go.transform.SetSiblingIndex((int)si);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);
            return ObjectRef.ToRef(go);
        }

        static JToken DestroyGameObject(JObject args)
        {
            var resolved = ObjectRef.Resolve(args["gameObject"]);
            var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
            if (go == null) throw new ArgumentException("gameObject does not resolve to a GameObject");

            var scene = go.scene;
            Undo.SetCurrentGroupName($"MCP Destroy GameObject '{go.name}'");
            int g = Undo.GetCurrentGroup();
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(g);
            return new JObject { ["ok"] = true };
        }

        static JToken CreateGameObject(JObject args)
        {
            var name = (string)args["name"];
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("gameobject_create requires name");

            // Validate parent BEFORE creating the GameObject so a guard failure doesn't leak an orphan.
            GameObject parentGo = null;
            if (args["parent"] != null && args["parent"].Type != JTokenType.Null)
            {
                var resolved = ObjectRef.Resolve(args["parent"]);
                parentGo = resolved as GameObject ?? (resolved as Component)?.gameObject;
                if (parentGo == null) throw new ArgumentException("parent does not resolve to a GameObject");
                if (!parentGo.scene.IsValid())
                    throw new ArgumentException("parent is a Prefab Asset (not a scene object). Use prefab_open to enter Prefab edit mode, or pass a scene GameObject.");
            }

            Undo.SetCurrentGroupName($"MCP Create GameObject '{name}'");
            int undoGroup = Undo.GetCurrentGroup();

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);

            var sceneName = (string)args["sceneName"];
            if (parentGo != null)
            {
                Undo.SetTransformParent(go.transform, parentGo.transform, "Reparent " + name);
            }
            else if (!string.IsNullOrEmpty(sceneName))
            {
                var scene = EditorSceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid()) throw new ArgumentException($"Scene '{sceneName}' not loaded");
                EditorSceneManager.MoveGameObjectToScene(go, scene);
            }

            bool local = args["isLocalSpace"] == null || (bool)args["isLocalSpace"];
            Undo.RecordObject(go.transform, "Set Transform " + name);
            if (args["position"] is JObject pos)
            {
                var v = (Vector3)ValueCoercion.Coerce(pos, typeof(Vector3));
                if (local) go.transform.localPosition = v; else go.transform.position = v;
            }
            if (args["rotation"] is JObject rot)
            {
                var q = (Quaternion)ValueCoercion.Coerce(rot, typeof(Quaternion));
                if (local) go.transform.localRotation = q; else go.transform.rotation = q;
            }
            else if (args["eulerAngles"] is JObject ea)
            {
                var v = (Vector3)ValueCoercion.Coerce(ea, typeof(Vector3));
                if (local) go.transform.localEulerAngles = v; else go.transform.eulerAngles = v;
            }
            if (args["localScale"] is JObject sc)
                go.transform.localScale = (Vector3)ValueCoercion.Coerce(sc, typeof(Vector3));

            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            return ObjectRef.ToRef(go);
        }
    }
}

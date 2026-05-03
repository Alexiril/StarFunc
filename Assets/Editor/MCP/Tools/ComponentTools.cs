using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MCP.Protocol;
using MCP.Reflection;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class ComponentTools
    {
        static ComponentTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "components_get",
                Description = "List components on a GameObject with serialized member snapshots. memberDepth controls how deep the Serializer recurses into nested member STRUCTS (e.g. transform.parent.parent...) — it does NOT walk into child GameObjects. To include child GameObjects' components, set childDepth > 0; the response shape switches to a tree {root, children}. maxDepth is accepted as a legacy alias for memberDepth.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "gameObject" },
                    ["properties"] = new JObject
                    {
                        ["gameObject"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of GameObject" },
                        ["memberDepth"] = new JObject { ["type"] = "integer", ["default"] = 2, ["description"] = "Max recursion depth into nested member structs per component." },
                        ["maxDepth"] = new JObject { ["type"] = "integer", ["description"] = "Legacy alias for memberDepth." },
                        ["childDepth"] = new JObject { ["type"] = "integer", ["default"] = 0, ["description"] = "If > 0, recurse into child GameObjects and return a tree." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => GetComponents(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "component_add",
                Description = "Add a component by type name (full or simple) to a GameObject.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "gameObject", "typeName" },
                    ["properties"] = new JObject
                    {
                        ["gameObject"] = new JObject { ["type"] = "object" },
                        ["typeName"] = new JObject { ["type"] = "string" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => AddComponent(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "component_remove",
                Description = "Remove a component from its GameObject. Checks RequireComponent constraints first. Supports Undo.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "component" },
                    ["properties"] = new JObject
                    {
                        ["component"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of the component to remove" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => RemoveComponent(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "component_set_enabled",
                Description = "Enable or disable a Behaviour component (anything that has an enabled flag).",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "component", "enabled" },
                    ["properties"] = new JObject
                    {
                        ["component"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of the Behaviour component" },
                        ["enabled"] = new JObject { ["type"] = "boolean" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => SetComponentEnabled(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "gameobject_get_component",
                Description = "Get a single Component on a GameObject by type. Skips the components_get round-trip when you only need one. Returns null in 'component' when not present. memberDepth controls Serializer recursion into nested member structs (default 2).",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "gameObject", "typeName" },
                    ["properties"] = new JObject
                    {
                        ["gameObject"] = new JObject { ["type"] = "object" },
                        ["typeName"] = new JObject { ["type"] = "string", ["description"] = "Full or simple Component type name (e.g. 'RectTransform', 'UnityEngine.UI.Button')." },
                        ["memberDepth"] = new JObject { ["type"] = "integer", ["default"] = 2 },
                        ["includeMembers"] = new JObject { ["type"] = "boolean", ["default"] = true }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => GetSingleComponent(args))
            });
        }

        static JToken GetComponents(JObject args)
        {
            var refToken = args["gameObject"];
            var resolved = ObjectRef.Resolve(refToken);
            var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
            if (go == null) throw new ArgumentException("gameObject does not resolve to a GameObject");

            int memberDepth = args["memberDepth"] != null ? (int)args["memberDepth"]
                            : (args["maxDepth"] != null ? (int)args["maxDepth"] : 2);
            int childDepth = args["childDepth"] != null ? (int)args["childDepth"] : 0;

            if (childDepth > 0)
                return new JObject { ["root"] = WalkTree(go, memberDepth, childDepth) };

            return new JObject { ["components"] = ComponentsArray(go, memberDepth) };
        }

        static JArray ComponentsArray(GameObject go, int memberDepth)
        {
            var arr = new JArray();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null)
                {
                    arr.Add(new JObject { ["type"] = "Missing", ["broken"] = true });
                    continue;
                }
                var entry = ObjectRef.ToRef(c);
                entry["members"] = SerializeMembers(c, memberDepth);
                arr.Add(entry);
            }
            return arr;
        }

        static JObject WalkTree(GameObject go, int memberDepth, int childDepth)
        {
            var node = new JObject
            {
                ["gameObject"] = ObjectRef.ToRef(go),
                ["components"] = ComponentsArray(go, memberDepth)
            };
            if (childDepth > 0 && go.transform.childCount > 0)
            {
                var children = new JArray();
                for (int i = 0; i < go.transform.childCount; i++)
                    children.Add(WalkTree(go.transform.GetChild(i).gameObject, memberDepth, childDepth - 1));
                node["children"] = children;
            }
            return node;
        }

        // Properties whose getters silently clone shared assets (leak warning + memory leak in edit mode).
        // The shared* counterparts are read instead via normal iteration.
        static readonly HashSet<string> InstantiatingGetters = new HashSet<string>
        {
            "UnityEngine.Renderer.material",
            "UnityEngine.Renderer.materials",
            "UnityEngine.MeshFilter.mesh",
            "UnityEngine.Collider.material",
        };

        static bool IsInstantiatingGetter(Type declaringType, string name)
            => InstantiatingGetters.Contains(declaringType.FullName + "." + name);

        static JObject SerializeMembers(Component c, int maxDepth)
        {
            var jo = new JObject();
            var type = c.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    bool serializable = f.IsPublic || f.IsDefined(typeof(SerializeField), inherit: true);
                    if (!serializable) continue;
                    if (jo.ContainsKey(f.Name)) continue;
                    try { jo[f.Name] = Serializer.Serialize(f.GetValue(c), maxDepth); }
                    catch (Exception e) { jo[f.Name] = "<error: " + e.Message + ">"; }
                }
                foreach (var p in t.GetProperties(flags | BindingFlags.DeclaredOnly))
                {
                    if (!p.CanRead) continue;
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (p.IsDefined(typeof(ObsoleteAttribute), inherit: true)) continue;
                    if (jo.ContainsKey(p.Name)) continue;
                    if (IsInstantiatingGetter(t, p.Name)) continue;
                    try { jo[p.Name] = Serializer.Serialize(p.GetValue(c), maxDepth); }
                    catch (Exception) { /* property getters can throw; skip silently */ }
                }
            }
            return jo;
        }

        static JToken RemoveComponent(JObject args)
        {
            var resolved = ObjectRef.Resolve(args["component"]);
            var component = resolved as Component;
            if (component == null) throw new ArgumentException("component does not resolve to a Component");

            var go = component.gameObject;
            var compType = component.GetType();

            foreach (var other in go.GetComponents<Component>())
            {
                if (other == null || other == component) continue;
                foreach (RequireComponent attr in other.GetType().GetCustomAttributes(typeof(RequireComponent), inherit: true))
                {
                    if ((attr.m_Type0 != null && attr.m_Type0.IsAssignableFrom(compType)) ||
                        (attr.m_Type1 != null && attr.m_Type1.IsAssignableFrom(compType)) ||
                        (attr.m_Type2 != null && attr.m_Type2.IsAssignableFrom(compType)))
                        throw new InvalidOperationException($"Cannot remove {compType.Name}: required by {other.GetType().Name}");
                }
            }

            var scene = go.scene;
            Undo.SetCurrentGroupName($"MCP Remove {compType.Name}");
            int g = Undo.GetCurrentGroup();
            Undo.DestroyObjectImmediate(component);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(g);
            return new JObject { ["ok"] = true };
        }

        static JToken SetComponentEnabled(JObject args)
        {
            var resolved = ObjectRef.Resolve(args["component"]);
            var behaviour = resolved as Behaviour;
            if (behaviour == null)
            {
                var c = resolved as Component;
                throw new ArgumentException(c != null
                    ? $"{c.GetType().Name} is not a Behaviour and cannot be toggled"
                    : "component does not resolve to a Behaviour");
            }

            bool enabled = (bool)args["enabled"];
            Undo.RecordObject(behaviour, $"MCP Set {behaviour.GetType().Name} enabled={enabled}");
            behaviour.enabled = enabled;
            EditorSceneManager.MarkSceneDirty(behaviour.gameObject.scene);
            return new JObject { ["ok"] = true, ["enabled"] = enabled };
        }

        static JToken GetSingleComponent(JObject args)
        {
            var resolved = ObjectRef.Resolve(args["gameObject"]);
            var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
            if (go == null) throw new ArgumentException("gameObject does not resolve to a GameObject");

            var typeName = (string)args["typeName"];
            if (string.IsNullOrEmpty(typeName)) throw new ArgumentException("typeName required");

            var matches = TypeUtil.ResolveAll(typeName);
            matches.RemoveAll(t => !typeof(Component).IsAssignableFrom(t));
            if (matches.Count == 0) throw new ArgumentException($"No Component type matches '{typeName}'");
            if (matches.Count > 1)
            {
                var names = new JArray();
                foreach (var t in matches) names.Add(t.AssemblyQualifiedName);
                throw new ArgumentException($"Ambiguous typeName '{typeName}'. Candidates: {string.Join(", ", names)}");
            }

            var c = go.GetComponent(matches[0]);
            if (c == null)
                return new JObject { ["found"] = false, ["typeName"] = matches[0].FullName, ["component"] = null };

            int memberDepth = args["memberDepth"] != null ? (int)args["memberDepth"] : 2;
            bool includeMembers = args["includeMembers"] == null || (bool)args["includeMembers"];

            var entry = ObjectRef.ToRef(c);
            if (includeMembers) entry["members"] = SerializeMembers(c, memberDepth);
            return new JObject { ["found"] = true, ["typeName"] = matches[0].FullName, ["component"] = entry };
        }

        static JToken AddComponent(JObject args)
        {
            var resolved = ObjectRef.Resolve(args["gameObject"]);
            var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
            if (go == null) throw new ArgumentException("gameObject does not resolve to a GameObject");

            var typeName = (string)args["typeName"];
            if (string.IsNullOrEmpty(typeName)) throw new ArgumentException("typeName required");

            var matches = TypeUtil.ResolveAll(typeName);
            matches.RemoveAll(t => !typeof(Component).IsAssignableFrom(t));
            if (matches.Count == 0) throw new ArgumentException($"No Component type matches '{typeName}'");
            if (matches.Count > 1)
            {
                var names = new JArray();
                foreach (var t in matches) names.Add(t.AssemblyQualifiedName);
                throw new ArgumentException($"Ambiguous type '{typeName}'. Candidates: {string.Join(", ", names)}");
            }
            var type = matches[0];

            if (type.IsDefined(typeof(DisallowMultipleComponent), inherit: true) && go.GetComponent(type) != null)
                throw new InvalidOperationException($"DisallowMultipleComponent: {type.Name} already present");

            Undo.SetCurrentGroupName($"MCP Add {type.Name}");
            int g = Undo.GetCurrentGroup();
            var component = Undo.AddComponent(go, type);
            if (PrefabUtility.IsPartOfPrefabInstance(component))
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(g);

            return ObjectRef.ToRef(component);
        }
    }
}

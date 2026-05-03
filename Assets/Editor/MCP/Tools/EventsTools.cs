using System;
using System.Reflection;
using MCP.Protocol;
using MCP.Reflection;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class EventsTools
    {
        static EventsTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "unityevent_add_persistent_listener",
                Description = "Add a persistent (inspector-visible) listener to a UnityEvent. Builds the call delegate from callTarget + methodName and dispatches to the matching UnityEventTools.Add*PersistentListener overload. argType: void | bool | int | float | string | object. argValue is required for non-void argTypes (for 'object', pass an ObjectRef).",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "target", "memberName", "callTarget", "methodName" },
                    ["properties"] = new JObject
                    {
                        ["target"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of the object holding the UnityEvent (e.g. a Button component)." },
                        ["memberName"] = new JObject { ["type"] = "string", ["description"] = "Field or property name of the UnityEvent on target (e.g. 'onClick')." },
                        ["callTarget"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of the object whose public method will be invoked (typically a Component on the same GameObject or another)." },
                        ["methodName"] = new JObject { ["type"] = "string", ["description"] = "Public method name on callTarget. Must return void and have a signature matching argType." },
                        ["argType"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "void", "bool", "int", "float", "string", "object" },
                            ["default"] = "void"
                        },
                        ["argValue"] = new JObject { ["description"] = "Bound argument passed to the listener. Required for non-void argType. For 'object', pass an ObjectRef." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => AddPersistentListener(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "unityevent_list_persistent_listeners",
                Description = "List the persistent (inspector-wired) listeners on a UnityEvent. Returns each entry's index, target ObjectRef, method name, and call state.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "target", "memberName" },
                    ["properties"] = new JObject
                    {
                        ["target"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of the object holding the UnityEvent." },
                        ["memberName"] = new JObject { ["type"] = "string", ["description"] = "Field/property name of the UnityEvent on target." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => ListPersistentListeners(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "unityevent_remove_persistent_listener",
                Description = "Remove a single persistent listener by index. Use unityevent_list_persistent_listeners to see indices first.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "target", "memberName", "index" },
                    ["properties"] = new JObject
                    {
                        ["target"] = new JObject { ["type"] = "object" },
                        ["memberName"] = new JObject { ["type"] = "string" },
                        ["index"] = new JObject { ["type"] = "integer" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => RemovePersistentListener(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "unityevent_clear_persistent_listeners",
                Description = "Remove all persistent listeners from a UnityEvent.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "target", "memberName" },
                    ["properties"] = new JObject
                    {
                        ["target"] = new JObject { ["type"] = "object" },
                        ["memberName"] = new JObject { ["type"] = "string" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => ClearPersistentListeners(args))
            });
        }

        static JToken AddPersistentListener(JObject args)
        {
            var targetObj = ObjectRef.Resolve(args["target"]);
            if (targetObj == null) throw new ArgumentException("target does not resolve");
            if (targetObj is GameObject)
                throw new ArgumentException("target must be a Component (or other UnityEngine.Object) holding the UnityEvent — not a GameObject");

            var memberName = (string)args["memberName"];
            if (string.IsNullOrEmpty(memberName)) throw new ArgumentException("memberName required");

            var unityEvent = ReadMember(targetObj, memberName);
            if (unityEvent == null)
                throw new ArgumentException($"{targetObj.GetType().Name}.{memberName} is null or not found");
            if (!(unityEvent is UnityEventBase))
                throw new ArgumentException($"{targetObj.GetType().Name}.{memberName} is {unityEvent.GetType().Name}, not a UnityEvent");

            var callTargetObj = ObjectRef.Resolve(args["callTarget"]);
            if (callTargetObj == null) throw new ArgumentException("callTarget does not resolve");
            if (callTargetObj is GameObject)
                throw new ArgumentException("callTarget must be a Component (the method's declaring object) — not a GameObject");

            var methodName = (string)args["methodName"];
            if (string.IsNullOrEmpty(methodName)) throw new ArgumentException("methodName required");

            var argType = ((string)args["argType"] ?? "void").ToLowerInvariant();

            Undo.SetCurrentGroupName($"MCP Add Persistent Listener {targetObj.GetType().Name}.{memberName}");
            int undoGroup = Undo.GetCurrentGroup();
            Undo.RecordObject(targetObj, "Add Persistent Listener");

            switch (argType)
            {
                case "void":
                {
                    if (!(unityEvent is UnityEvent ev))
                        throw new InvalidOperationException($"argType=void requires UnityEvent (no generic), got {unityEvent.GetType().Name}");
                    var del = (UnityAction)BuildDelegate(typeof(UnityAction), callTargetObj, methodName);
                    UnityEventTools.AddVoidPersistentListener(ev, del);
                    break;
                }
                case "bool":
                {
                    if (!(unityEvent is UnityEvent<bool> ev))
                        throw new InvalidOperationException($"argType=bool requires UnityEvent<bool>, got {unityEvent.GetType().Name}");
                    var del = (UnityAction<bool>)BuildDelegate(typeof(UnityAction<bool>), callTargetObj, methodName);
                    var v = (bool)ValueCoercion.Coerce(RequireArgValue(args), typeof(bool));
                    UnityEventTools.AddBoolPersistentListener(ev, del, v);
                    break;
                }
                case "int":
                {
                    if (!(unityEvent is UnityEvent<int> ev))
                        throw new InvalidOperationException($"argType=int requires UnityEvent<int>, got {unityEvent.GetType().Name}");
                    var del = (UnityAction<int>)BuildDelegate(typeof(UnityAction<int>), callTargetObj, methodName);
                    var v = (int)ValueCoercion.Coerce(RequireArgValue(args), typeof(int));
                    UnityEventTools.AddIntPersistentListener(ev, del, v);
                    break;
                }
                case "float":
                {
                    if (!(unityEvent is UnityEvent<float> ev))
                        throw new InvalidOperationException($"argType=float requires UnityEvent<float>, got {unityEvent.GetType().Name}");
                    var del = (UnityAction<float>)BuildDelegate(typeof(UnityAction<float>), callTargetObj, methodName);
                    var v = (float)ValueCoercion.Coerce(RequireArgValue(args), typeof(float));
                    UnityEventTools.AddFloatPersistentListener(ev, del, v);
                    break;
                }
                case "string":
                {
                    if (!(unityEvent is UnityEvent<string> ev))
                        throw new InvalidOperationException($"argType=string requires UnityEvent<string>, got {unityEvent.GetType().Name}");
                    var del = (UnityAction<string>)BuildDelegate(typeof(UnityAction<string>), callTargetObj, methodName);
                    var v = (string)ValueCoercion.Coerce(RequireArgValue(args), typeof(string));
                    UnityEventTools.AddStringPersistentListener(ev, del, v);
                    break;
                }
                case "object":
                {
                    if (!(unityEvent is UnityEvent<Object> ev))
                        throw new InvalidOperationException($"argType=object requires UnityEvent<UnityEngine.Object>, got {unityEvent.GetType().Name}");
                    var del = (UnityAction<Object>)BuildDelegate(typeof(UnityAction<Object>), callTargetObj, methodName);
                    var v = (Object)ValueCoercion.Coerce(RequireArgValue(args), typeof(Object));
                    UnityEventTools.AddObjectPersistentListener(ev, del, v);
                    break;
                }
                default:
                    throw new ArgumentException($"Unsupported argType: {argType}. Valid: void, bool, int, float, string, object.");
            }

            EditorUtility.SetDirty(targetObj);
            if (PrefabUtility.IsPartOfPrefabInstance(targetObj))
                PrefabUtility.RecordPrefabInstancePropertyModifications(targetObj);
            if (targetObj is Component comp && comp.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);

            Undo.CollapseUndoOperations(undoGroup);

            var ub = (UnityEventBase)unityEvent;
            return new JObject
            {
                ["ok"] = true,
                ["target"] = ObjectRef.ToRef(targetObj),
                ["unityEventField"] = memberName,
                ["call"] = $"{callTargetObj.GetType().Name}.{methodName}",
                ["argType"] = argType,
                ["persistentListenerCount"] = ub.GetPersistentEventCount()
            };
        }

        static Delegate BuildDelegate(Type delegateType, object target, string methodName)
        {
            try { return Delegate.CreateDelegate(delegateType, target, methodName); }
            catch (ArgumentException e)
            {
                throw new ArgumentException(
                    $"Cannot bind {target.GetType().Name}.{methodName} to {delegateType.Name}: {e.Message}. " +
                    "The method must be public, void-returning, with arguments matching argType.");
            }
        }

        static JToken RequireArgValue(JObject args)
        {
            var v = args["argValue"];
            if (v == null || v.Type == JTokenType.Null)
                throw new ArgumentException("argValue is required for non-void argType");
            return v;
        }

        static object ReadMember(object target, string memberName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                var f = t.GetField(memberName, flags | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(target);
                var p = t.GetProperty(memberName, flags | BindingFlags.DeclaredOnly);
                if (p != null && p.CanRead) return p.GetValue(target);
            }
            return null;
        }

        static UnityEventBase ResolveEvent(JObject args, out Object targetObj, out string memberName)
        {
            targetObj = ObjectRef.Resolve(args["target"]);
            if (targetObj == null) throw new ArgumentException("target does not resolve");
            if (targetObj is GameObject)
                throw new ArgumentException("target must be a Component (or other UnityEngine.Object) holding the UnityEvent — not a GameObject");

            memberName = (string)args["memberName"];
            if (string.IsNullOrEmpty(memberName)) throw new ArgumentException("memberName required");

            var member = ReadMember(targetObj, memberName);
            if (member == null)
                throw new ArgumentException($"{targetObj.GetType().Name}.{memberName} is null or not found");
            if (!(member is UnityEventBase ueb))
                throw new ArgumentException($"{targetObj.GetType().Name}.{memberName} is {member.GetType().Name}, not a UnityEvent");

            return ueb;
        }

        static void MarkDirtyAfterEventEdit(Object targetObj)
        {
            EditorUtility.SetDirty(targetObj);
            if (PrefabUtility.IsPartOfPrefabInstance(targetObj))
                PrefabUtility.RecordPrefabInstancePropertyModifications(targetObj);
            if (targetObj is Component comp && comp.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
        }

        static JToken ListPersistentListeners(JObject args)
        {
            var ev = ResolveEvent(args, out var targetObj, out var memberName);
            int count = ev.GetPersistentEventCount();
            var arr = new JArray();
            for (int i = 0; i < count; i++)
            {
                var t = ev.GetPersistentTarget(i);
                arr.Add(new JObject
                {
                    ["index"] = i,
                    ["methodName"] = ev.GetPersistentMethodName(i),
                    ["callState"] = ev.GetPersistentListenerState(i).ToString(),
                    ["target"] = t != null ? ObjectRef.ToRef(t) : null
                });
            }
            return new JObject
            {
                ["target"] = ObjectRef.ToRef(targetObj),
                ["unityEventField"] = memberName,
                ["count"] = count,
                ["listeners"] = arr
            };
        }

        static JToken RemovePersistentListener(JObject args)
        {
            var ev = ResolveEvent(args, out var targetObj, out var memberName);
            if (args["index"] == null || args["index"].Type != JTokenType.Integer)
                throw new ArgumentException("index (integer) required");
            int index = (int)args["index"];
            int count = ev.GetPersistentEventCount();
            if (index < 0 || index >= count)
                throw new ArgumentException($"index {index} out of range [0, {count})");

            Undo.RecordObject(targetObj, $"MCP Remove Persistent Listener {targetObj.GetType().Name}.{memberName}");
            UnityEventTools.RemovePersistentListener(ev, index);
            MarkDirtyAfterEventEdit(targetObj);

            return new JObject
            {
                ["ok"] = true,
                ["removedIndex"] = index,
                ["remainingCount"] = ev.GetPersistentEventCount()
            };
        }

        static JToken ClearPersistentListeners(JObject args)
        {
            var ev = ResolveEvent(args, out var targetObj, out var memberName);
            int initial = ev.GetPersistentEventCount();
            if (initial == 0)
                return new JObject { ["ok"] = true, ["clearedCount"] = 0 };

            Undo.RecordObject(targetObj, $"MCP Clear Persistent Listeners {targetObj.GetType().Name}.{memberName}");
            while (ev.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(ev, 0);
            MarkDirtyAfterEventEdit(targetObj);

            return new JObject { ["ok"] = true, ["clearedCount"] = initial };
        }
    }
}

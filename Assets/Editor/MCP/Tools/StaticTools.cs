using System;
using System.Collections;
using System.Reflection;
using MCP.Protocol;
using MCP.Reflection;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class StaticTools
    {
        static StaticTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "static_invoke",
                Description = "Read a static field/property OR call a static method on a type. typeName is the full or simple .NET type name (e.g. 'UnityEditor.EditorPrefs', 'PlayerSettings', 'UnityEditor.Selection'). methodArgs[] makes it a method call; omit to read a static field/property or invoke a no-arg method. Generic methods (e.g. AssetDatabase.LoadAssetAtPath<T>) require genericArgs: an array of type names like ['UnityEngine.Material']. For System.Type parameters (e.g. Resources.Load(string, Type)), pass {\"__type\":\"FullName\"} as the methodArgs entry. Enum args accept the enum name as a string or its int value. NOTE: static calls are NOT Undo-tracked — Unity's Undo system only captures UnityEngine.Object mutations.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "typeName", "memberName" },
                    ["properties"] = new JObject
                    {
                        ["typeName"] = new JObject { ["type"] = "string", ["description"] = "Full or simple .NET type name." },
                        ["memberName"] = new JObject { ["type"] = "string" },
                        ["methodArgs"] = new JObject { ["type"] = "array", ["description"] = "Pass to invoke a method; omit to read a static field/property." },
                        ["genericArgs"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Type-arg names for generic methods (e.g. ['UnityEngine.Material'] for GetBuiltinExtraResource<Material>)." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => InvokeStatic(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "editor_execute_menu_item",
                Description = "Execute a Unity menu item by its full path (e.g. 'Assets/Reimport All', 'Window/Rendering/Lighting'). Wraps EditorApplication.ExecuteMenuItem.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "path" },
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string", ["description"] = "Slash-separated menu path." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => ExecuteMenuItem(args))
            });
        }

        static JToken InvokeStatic(JObject args)
        {
            var typeName = (string)args["typeName"];
            if (string.IsNullOrEmpty(typeName)) throw new ArgumentException("typeName required");
            var memberName = (string)args["memberName"];
            if (string.IsNullOrEmpty(memberName)) throw new ArgumentException("memberName required");

            var matches = TypeUtil.ResolveAll(typeName);
            if (matches.Count == 0) throw new ArgumentException($"Type not found: {typeName}");
            if (matches.Count > 1)
            {
                var names = new JArray();
                foreach (var t in matches) names.Add(t.AssemblyQualifiedName);
                throw new ArgumentException($"Ambiguous type '{typeName}'. Candidates: {string.Join(", ", names)}");
            }
            var type = matches[0];

            var methodArgs = args["methodArgs"] as JArray;
            var genericArgs = args["genericArgs"] as JArray;
            var resolved = MemberResolver.Resolve(type, memberName, methodArgs, isStatic: true);

            switch (resolved.Kind)
            {
                case MemberKind.Field:
                    if (methodArgs != null) throw new ArgumentException($"'{memberName}' is a static field, not a method");
                    if (genericArgs != null) throw new ArgumentException("genericArgs only valid for methods");
                    return Wrap(Serializer.Serialize(resolved.Field.GetValue(null)));

                case MemberKind.Property:
                    if (methodArgs != null) throw new ArgumentException($"'{memberName}' is a static property, not a method");
                    if (genericArgs != null) throw new ArgumentException("genericArgs only valid for methods");
                    if (!resolved.Property.CanRead) throw new InvalidOperationException($"Static property '{memberName}' is write-only");
                    return Wrap(Serializer.Serialize(resolved.Property.GetValue(null)));

                case MemberKind.Method:
                    var method = TypeUtil.CloseGeneric(resolved.Method, genericArgs);
                    return CallStatic(method, methodArgs ?? new JArray());

                default:
                    throw new ArgumentException($"Static member '{memberName}' not found on {type.FullName}");
            }
        }

        static JToken CallStatic(MethodInfo method, JArray jArgs)
        {
            if (typeof(IEnumerator).IsAssignableFrom(method.ReturnType))
                throw new InvalidOperationException("IEnumerator-returning methods (coroutines) are not supported");

            var pars = method.GetParameters();
            if (pars.Length != jArgs.Count)
                throw new ArgumentException($"Static method '{method.Name}' expects {pars.Length} args, got {jArgs.Count}");

            var coerced = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                coerced[i] = ValueCoercion.Coerce(jArgs[i], pars[i].ParameterType);

            object result;
            try { result = method.Invoke(null, coerced); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }

            return Wrap(method.ReturnType == typeof(void) ? JValue.CreateNull() : Serializer.Serialize(result));
        }

        static JToken ExecuteMenuItem(JObject args)
        {
            var path = (string)args["path"];
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required");
            bool ok = EditorApplication.ExecuteMenuItem(path);
            return new JObject { ["ok"] = ok, ["path"] = path };
        }

        static JToken Wrap(JToken value) => new JObject { ["value"] = value };
    }
}

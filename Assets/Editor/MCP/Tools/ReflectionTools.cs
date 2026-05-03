using System;
using System.Collections;
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
    public static class ReflectionTools
    {
        static ReflectionTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "member_invoke",
                Description = "Read a field/property OR call a method on a Component. methodArgs[] makes it a method call. Generic methods (e.g. GameObject.GetComponent<T>()) require genericArgs: an array of type names like ['UnityEngine.Light']. For System.Type parameters, pass {\"__type\":\"FullName\"} as the methodArgs entry. Enum args accept the enum name as a string (e.g. \"Center\", \"MiddleCenter\") or its int value. Object refs can be passed as {__ref:true,instanceId:...}.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "target", "memberName" },
                    ["properties"] = new JObject
                    {
                        ["target"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of Component (or any UnityEngine.Object)" },
                        ["memberName"] = new JObject { ["type"] = "string" },
                        ["methodArgs"] = new JObject { ["type"] = "array", ["description"] = "Pass to invoke a method; omit to read field/property." },
                        ["genericArgs"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Type-arg names for generic methods (e.g. ['UnityEngine.Light'] for GetComponent<Light>)." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => Invoke(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "member_set",
                Description = "Set a field/property on a Component. Supports primitives, Unity structs (Vector3/Color/etc.), enums (pass the name as a string like \"Center\" or its int value), arrays/lists, and Object references via {__ref:true,instanceId:...}.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "target", "memberName", "value" },
                    ["properties"] = new JObject
                    {
                        ["target"] = new JObject { ["type"] = "object" },
                        ["memberName"] = new JObject { ["type"] = "string" },
                        ["value"] = new JObject { ["description"] = "Coerced to member type. Use {__ref:true,instanceId:...} for Object refs." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => SetMember(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "member_set_batch",
                Description = "Apply many member_set operations atomically (single Undo group). Continues on per-item error and returns per-item results — failed items don't abort the batch.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "items" },
                    ["properties"] = new JObject
                    {
                        ["items"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Array of {target, memberName, value} objects, same shape as member_set.",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["required"] = new JArray { "target", "memberName", "value" },
                                ["properties"] = new JObject
                                {
                                    ["target"] = new JObject { ["type"] = "object" },
                                    ["memberName"] = new JObject { ["type"] = "string" },
                                    ["value"] = new JObject()
                                }
                            }
                        }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => SetMemberBatch(args))
            });
        }

        static JToken Invoke(JObject args)
        {
            var target = ObjectRef.Resolve(args["target"]);
            if (target == null) throw new ArgumentException("target does not resolve");
            var memberName = (string)args["memberName"];
            if (string.IsNullOrEmpty(memberName)) throw new ArgumentException("memberName required");

            var methodArgs = args["methodArgs"] as JArray;
            var genericArgs = args["genericArgs"] as JArray;
            var resolved = MemberResolver.Resolve(target.GetType(), memberName, methodArgs);

            switch (resolved.Kind)
            {
                case MemberKind.Field:
                    if (methodArgs != null) throw new ArgumentException($"'{memberName}' is a field, not a method");
                    if (genericArgs != null) throw new ArgumentException("genericArgs only valid for methods");
                    return Wrap(Serializer.Serialize(resolved.Field.GetValue(target)));

                case MemberKind.Property:
                    if (methodArgs != null) throw new ArgumentException($"'{memberName}' is a property, not a method");
                    if (genericArgs != null) throw new ArgumentException("genericArgs only valid for methods");
                    return Wrap(Serializer.Serialize(resolved.Property.GetValue(target)));

                case MemberKind.Method:
                    var method = TypeUtil.CloseGeneric(resolved.Method, genericArgs);
                    return CallMethod(target, method, methodArgs ?? new JArray());

                default:
                    throw new ArgumentException($"Member '{memberName}' not found on {target.GetType().FullName}");
            }
        }

        static JToken CallMethod(object target, MethodInfo method, JArray jArgs)
        {
            if (typeof(IEnumerator).IsAssignableFrom(method.ReturnType))
                throw new InvalidOperationException("IEnumerator-returning methods (coroutines) are not supported");

            var pars = method.GetParameters();
            if (pars.Length != jArgs.Count)
                throw new ArgumentException($"Method '{method.Name}' expects {pars.Length} args, got {jArgs.Count}");

            var coerced = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                coerced[i] = ValueCoercion.Coerce(jArgs[i], pars[i].ParameterType);

            bool isMutation = method.ReturnType == typeof(void) && target is Object uo;
            if (isMutation)
            {
                Undo.RecordObject((Object)target, $"MCP Invoke {method.Name}");
            }

            object result;
            try { result = method.Invoke(target, coerced); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }

            if (isMutation)
            {
                EditorUtility.SetDirty((Object)target);
                if (target is Component cmp && cmp != null)
                    EditorSceneManager.MarkSceneDirty(cmp.gameObject.scene);
            }

            return Wrap(method.ReturnType == typeof(void) ? JValue.CreateNull() : Serializer.Serialize(result));
        }

        static JToken Wrap(JToken value) => new JObject { ["value"] = value };

        static JToken SetMember(JObject args)
        {
            var target = ObjectRef.Resolve(args["target"]);
            if (target == null) throw new ArgumentException("target does not resolve");
            var memberName = (string)args["memberName"];
            if (string.IsNullOrEmpty(memberName)) throw new ArgumentException("memberName required");

            var value = args["value"];

            if (target is Object uo && uo != null)
            {
                if (TrySerializedPropertySet(uo, memberName, value))
                    return new JObject { ["ok"] = true, ["path"] = "SerializedProperty" };
            }

            return ReflectionSet(target, memberName, value);
        }

        static JToken SetMemberBatch(JObject args)
        {
            var items = args["items"] as JArray;
            if (items == null || items.Count == 0)
                throw new ArgumentException("items[] required and non-empty");

            Undo.IncrementCurrentGroup();
            int g = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"MCP member_set_batch ({items.Count} items)");

            var perItem = new JArray();
            int succeeded = 0, failed = 0;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i] as JObject;
                if (item == null)
                {
                    perItem.Add(new JObject { ["ok"] = false, ["error"] = "item must be an object" });
                    failed++;
                    continue;
                }

                try
                {
                    var sub = SetMember(item);
                    perItem.Add(sub);
                    succeeded++;
                }
                catch (Exception e)
                {
                    perItem.Add(new JObject { ["ok"] = false, ["error"] = e.Message, ["index"] = i });
                    failed++;
                }
            }

            Undo.CollapseUndoOperations(g);

            return new JObject
            {
                ["totalItems"] = items.Count,
                ["succeeded"] = succeeded,
                ["failed"] = failed,
                ["items"] = perItem
            };
        }

        static bool TrySerializedPropertySet(Object uo, string name, JToken value)
        {
            var so = new SerializedObject(uo);
            var prop = so.FindProperty(name);
            if (prop == null) return false;

            try
            {
                ApplyToSerializedProperty(prop, value);
                so.ApplyModifiedProperties();
                if (uo is Component cmp && cmp != null)
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(cmp))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(cmp);
                    EditorSceneManager.MarkSceneDirty(cmp.gameObject.scene);
                }
                return true;
            }
            catch (NotSupportedException) { return false; }
        }

        static void ApplyToSerializedProperty(SerializedProperty prop, JToken value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: prop.intValue = (int)value; return;
                case SerializedPropertyType.Boolean: prop.boolValue = (bool)value; return;
                case SerializedPropertyType.Float: prop.floatValue = (float)value; return;
                case SerializedPropertyType.String: prop.stringValue = (string)value; return;
                case SerializedPropertyType.Color: prop.colorValue = (Color)ValueCoercion.Coerce(value, typeof(Color)); return;
                case SerializedPropertyType.Vector2: prop.vector2Value = (Vector2)ValueCoercion.Coerce(value, typeof(Vector2)); return;
                case SerializedPropertyType.Vector3: prop.vector3Value = (Vector3)ValueCoercion.Coerce(value, typeof(Vector3)); return;
                case SerializedPropertyType.Vector4: prop.vector4Value = (Vector4)ValueCoercion.Coerce(value, typeof(Vector4)); return;
                case SerializedPropertyType.Vector2Int: prop.vector2IntValue = (Vector2Int)ValueCoercion.Coerce(value, typeof(Vector2Int)); return;
                case SerializedPropertyType.Vector3Int: prop.vector3IntValue = (Vector3Int)ValueCoercion.Coerce(value, typeof(Vector3Int)); return;
                case SerializedPropertyType.Quaternion: prop.quaternionValue = (Quaternion)ValueCoercion.Coerce(value, typeof(Quaternion)); return;
                case SerializedPropertyType.Rect: prop.rectValue = (Rect)ValueCoercion.Coerce(value, typeof(Rect)); return;
                case SerializedPropertyType.Bounds: prop.boundsValue = (Bounds)ValueCoercion.Coerce(value, typeof(Bounds)); return;
                case SerializedPropertyType.Enum:
                    if (value.Type == JTokenType.Integer) prop.enumValueIndex = (int)value;
                    else if (value.Type == JTokenType.String)
                    {
                        var idx = Array.IndexOf(prop.enumNames, (string)value);
                        if (idx < 0) throw new ArgumentException($"Unknown enum value '{value}' for {prop.propertyPath}");
                        prop.enumValueIndex = idx;
                    }
                    else if (value is JObject ej && ej["__enum"] != null)
                    {
                        var idx = Array.IndexOf(prop.enumNames, (string)ej["__enum"]);
                        if (idx < 0) throw new ArgumentException($"Unknown enum value '{ej["__enum"]}'");
                        prop.enumValueIndex = idx;
                    }
                    return;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = (int)((LayerMask)ValueCoercion.Coerce(value, typeof(LayerMask)));
                    return;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = ObjectRef.ResolveStrict(value);
                    return;
                case SerializedPropertyType.ArraySize:
                    prop.intValue = (int)value;
                    return;
                case SerializedPropertyType.Generic:
                    if (prop.isArray)
                    {
                        var arr = (JArray)value;
                        prop.arraySize = arr.Count;
                        for (int i = 0; i < arr.Count; i++)
                            ApplyToSerializedProperty(prop.GetArrayElementAtIndex(i), arr[i]);
                        return;
                    }
                    throw new NotSupportedException("Generic non-array SerializedProperty not supported via fast path");
                default:
                    throw new NotSupportedException($"SerializedPropertyType {prop.propertyType} not supported");
            }
        }

        static JToken ReflectionSet(object target, string memberName, JToken value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = target.GetType();

            FieldInfo field = null;
            PropertyInfo prop = null;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                field = t.GetField(memberName, flags | BindingFlags.DeclaredOnly);
                if (field != null) break;
                prop = t.GetProperty(memberName, flags | BindingFlags.DeclaredOnly);
                if (prop != null) break;
            }

            Undo.IncrementCurrentGroup();
            int g = Undo.GetCurrentGroup();

            if (target is Object uoTarget && uoTarget != null)
                Undo.RecordObject(uoTarget, $"MCP Set {memberName}");

            if (field != null)
            {
                var coerced = ValueCoercion.Coerce(value, field.FieldType);
                field.SetValue(target, coerced);
            }
            else if (prop != null)
            {
                if (!prop.CanWrite) throw new InvalidOperationException($"Property '{memberName}' is read-only");
                var coerced = ValueCoercion.Coerce(value, prop.PropertyType);
                prop.SetValue(target, coerced);
            }
            else
            {
                throw new ArgumentException($"Member '{memberName}' not found on {type.FullName}");
            }

            if (target is Object uo && uo != null)
            {
                EditorUtility.SetDirty(uo);
                if (uo is Component cmp && cmp != null)
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(cmp))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(cmp);
                    EditorSceneManager.MarkSceneDirty(cmp.gameObject.scene);
                }
            }
            Undo.CollapseUndoOperations(g);

            return new JObject { ["ok"] = true, ["path"] = "Reflection" };
        }
    }
}

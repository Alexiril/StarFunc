using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCP.Reflection
{
    public static class TypeUtil
    {
        static Dictionary<string, Type> _cache = new Dictionary<string, Type>();

        public static Type Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_cache.TryGetValue(name, out var hit)) return hit;

            var t = Type.GetType(name, throwOnError: false);
            if (t != null) { _cache[name] = t; return t; }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(name, throwOnError: false);
                if (t != null) { _cache[name] = t; return t; }
            }

            foreach (var candidate in TypeCache.GetTypesDerivedFrom<UnityEngine.Object>())
            {
                if (candidate.FullName == name || candidate.Name == name)
                {
                    _cache[name] = candidate;
                    return candidate;
                }
            }

            return null;
        }

        public static List<Type> ResolveAll(string name)
        {
            var list = new List<Type>();
            if (string.IsNullOrEmpty(name)) return list;
            var seen = new HashSet<Type>();
            var direct = Type.GetType(name, false);
            if (direct != null && seen.Add(direct)) list.Add(direct);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name, false);
                if (t != null && seen.Add(t)) list.Add(t);
            }
            foreach (var candidate in TypeCache.GetTypesDerivedFrom<UnityEngine.Object>())
            {
                if ((candidate.FullName == name || candidate.Name == name) && seen.Add(candidate))
                    list.Add(candidate);
            }
            return list;
        }

        // Closes a generic method definition over the type names in genericArgs (each entry is a full
        // or simple type name). Returns the input unchanged when both the method is non-generic and
        // genericArgs is empty/null. Throws on any mismatch — that's the caller's signal to add or
        // drop genericArgs.
        public static MethodInfo CloseGeneric(MethodInfo method, JArray genericArgs)
        {
            bool isGeneric = method.IsGenericMethodDefinition;
            int provided = genericArgs?.Count ?? 0;

            if (!isGeneric && provided == 0) return method;
            if (!isGeneric && provided > 0)
                throw new ArgumentException($"genericArgs provided but method '{method.Name}' is not generic");
            if (isGeneric && provided == 0)
                throw new ArgumentException($"Method '{method.Name}' is generic; provide genericArgs (e.g. [\"UnityEngine.Material\"])");

            var genericParams = method.GetGenericArguments();
            if (genericParams.Length != provided)
                throw new ArgumentException($"Method '{method.Name}' expects {genericParams.Length} type args, got {provided}");

            var typeArgs = new Type[provided];
            for (int i = 0; i < provided; i++)
            {
                var n = (string)genericArgs[i];
                if (string.IsNullOrEmpty(n)) throw new ArgumentException("genericArgs entries must be non-empty type names");
                var matches = ResolveAll(n);
                if (matches.Count == 0) throw new ArgumentException($"genericArg type not found: {n}");
                if (matches.Count > 1) throw new ArgumentException($"genericArg type ambiguous: '{n}'. Use the AssemblyQualifiedName.");
                typeArgs[i] = matches[0];
            }
            return method.MakeGenericMethod(typeArgs);
        }
    }
}

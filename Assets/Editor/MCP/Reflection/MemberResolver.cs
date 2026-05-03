using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace MCP.Reflection
{
    public enum MemberKind { None, Field, Property, Method }

    public struct ResolvedMember
    {
        public MemberKind Kind;
        public FieldInfo Field;
        public PropertyInfo Property;
        public MethodInfo Method;

        public Type ValueType => Kind switch
        {
            MemberKind.Field => Field.FieldType,
            MemberKind.Property => Property.PropertyType,
            MemberKind.Method => Method.ReturnType,
            _ => null
        };
    }

    public static class MemberResolver
    {
        const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        public static ResolvedMember Resolve(Type type, string name, JArray methodArgs, bool isStatic = false)
        {
            if (isStatic) return ResolveStatic(type, name, methodArgs);

            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var f = t.GetField(name, InstanceFlags);
                if (f != null) return new ResolvedMember { Kind = MemberKind.Field, Field = f };

                var p = t.GetProperty(name, InstanceFlags);
                if (p != null) return new ResolvedMember { Kind = MemberKind.Property, Property = p };
            }

            if (methodArgs != null)
            {
                var candidates = new List<MethodInfo>();
                for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (var m in t.GetMethods(InstanceFlags))
                        if (m.Name == name && m.GetParameters().Length == methodArgs.Count)
                            candidates.Add(m);
                    if (candidates.Count > 0) break;
                }
                return PickOverload(candidates, methodArgs);
            }
            else
            {
                for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                    foreach (var m in t.GetMethods(InstanceFlags))
                        if (m.Name == name && m.GetParameters().Length == 0)
                            return new ResolvedMember { Kind = MemberKind.Method, Method = m };
            }

            return new ResolvedMember { Kind = MemberKind.None };
        }

        static ResolvedMember ResolveStatic(Type type, string name, JArray methodArgs)
        {
            var f = type.GetField(name, StaticFlags);
            if (f != null) return new ResolvedMember { Kind = MemberKind.Field, Field = f };

            var p = type.GetProperty(name, StaticFlags);
            if (p != null) return new ResolvedMember { Kind = MemberKind.Property, Property = p };

            if (methodArgs != null)
            {
                var candidates = new List<MethodInfo>();
                foreach (var m in type.GetMethods(StaticFlags))
                    if (m.Name == name && m.GetParameters().Length == methodArgs.Count)
                        candidates.Add(m);
                return PickOverload(candidates, methodArgs);
            }

            foreach (var m in type.GetMethods(StaticFlags))
                if (m.Name == name && m.GetParameters().Length == 0)
                    return new ResolvedMember { Kind = MemberKind.Method, Method = m };

            return new ResolvedMember { Kind = MemberKind.None };
        }

        static ResolvedMember PickOverload(List<MethodInfo> candidates, JArray methodArgs)
        {
            if (candidates.Count == 0) return new ResolvedMember { Kind = MemberKind.None };
            if (candidates.Count == 1) return new ResolvedMember { Kind = MemberKind.Method, Method = candidates[0] };

            MethodInfo best = candidates[0];
            int bestScore = ScoreOverload(best, methodArgs);
            for (int i = 1; i < candidates.Count; i++)
            {
                int s = ScoreOverload(candidates[i], methodArgs);
                if (s > bestScore) { best = candidates[i]; bestScore = s; }
            }
            return new ResolvedMember { Kind = MemberKind.Method, Method = best };
        }

        static int ScoreOverload(MethodInfo m, JArray args)
        {
            var pars = m.GetParameters();
            int score = 0;
            for (int i = 0; i < pars.Length; i++)
            {
                var pt = pars[i].ParameterType;
                var tok = args[i];
                if (pt == typeof(string) && tok.Type == JTokenType.String) score += 2;
                else if ((pt == typeof(int) || pt == typeof(long)) && tok.Type == JTokenType.Integer) score += 2;
                else if ((pt == typeof(float) || pt == typeof(double)) && (tok.Type == JTokenType.Float || tok.Type == JTokenType.Integer)) score += 2;
                else if (pt == typeof(bool) && tok.Type == JTokenType.Boolean) score += 2;
                else score += 1;
            }
            return score;
        }
    }
}

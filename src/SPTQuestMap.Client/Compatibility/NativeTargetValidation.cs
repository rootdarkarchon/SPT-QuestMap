using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SPTQuestMap.Client.Compatibility;

// This reflection-only validator is also run in a metadata-only test context: no EFT code executes there.
internal static class NativeTargetValidation
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static IReadOnlyList<string> Validate(
        Func<string, Type?> resolveType, IEnumerable<string> requirements, out IReadOnlyList<MethodInfo> patchMethods)
    {
        var failures = new List<string>();
        var patches = new List<MethodInfo>();
        foreach (var specification in requirements)
        {
            var parts = specification.Split('|');
            try
            {
                var type = resolveType(parts[1]);
                MemberInfo[] matches = type is null ? Array.Empty<MemberInfo>() : parts[0] switch
                {
                    "M" => type.GetMethods(Flags).Where(m => m.Name == parts[2]
                        && TypeKey(m.ReturnType) == parts[3]
                        && string.Join(";", m.GetParameters().Select(p => TypeKey(p.ParameterType))) == parts[4]
                        && m.IsStatic == (parts[5] == "static")).Cast<MemberInfo>().ToArray(),
                    "F" => type.GetFields(Flags).Where(f => f.Name == parts[2]
                        && TypeKey(f.FieldType) == parts[3] && f.IsStatic == (parts[5] == "static")).Cast<MemberInfo>().ToArray(),
                    "P" => type.GetProperties(Flags).Where(p => p.Name == parts[2]
                        && TypeKey(p.PropertyType) == parts[3]
                        && p.GetGetMethod(true)?.IsStatic == (parts[5] == "static")).Cast<MemberInfo>().ToArray(),
                    "E" => type.GetEvents(Flags).Where(e => e.Name == parts[2]
                        && TypeKey(e.EventHandlerType!) == parts[3]
                        && e.GetAddMethod(true)?.IsStatic == (parts[5] == "static")
                        && e.GetRemoveMethod(true) is not null).Cast<MemberInfo>().ToArray(),
                    _ => Array.Empty<MemberInfo>()
                };
                if (matches.Length != 1)
                    failures.Add($"Native contract mismatch: {parts[1]}.{parts[2]} ({parts[0]}, matches={matches.Length}).");
                else if (parts[6] == "patch") patches.Add((MethodInfo)matches[0]);
            }
            catch (Exception exception)
            {
                failures.Add($"Native contract inspection failed: {parts[1]}.{parts[2]}: {exception.GetType().Name}: {exception.Message}");
            }
        }
        patchMethods = patches;
        return failures;
    }

    private static string TypeKey(Type type)
    {
        if (type.IsByRef) return TypeKey(type.GetElementType()!) + "&";
        if (type.IsArray) return TypeKey(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return type.Name;
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeKey)) + ">";
    }
}

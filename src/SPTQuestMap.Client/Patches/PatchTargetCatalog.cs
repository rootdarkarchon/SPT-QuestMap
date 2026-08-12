using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.UI;

namespace SPTQuestMap.Client.Patches;

internal static class PatchTargetCatalog
{
    private static readonly string[] Names =
    {
        "MainMenuControllerClass.ShowScreen",
        "EFT.UI.QuestsScreen.Show",
        "EFT.UI.QuestsScreen.Close",
        "EFT.UI.TasksScreen.Show",
        "EFT.UI.TasksScreen.Close"
    };

    public static IReadOnlyList<string> TargetNames => Names;

    public static PatchTargetResolution ResolveExactTargets()
    {
        var specifications = new[]
        {
            new TargetSpec(typeof(MainMenuControllerClass), "ShowScreen", 2),
            new TargetSpec(typeof(QuestsScreen), nameof(QuestsScreen.Show), 4),
            new TargetSpec(typeof(QuestsScreen), nameof(QuestsScreen.Close), 0),
            new TargetSpec(typeof(TasksScreen), nameof(TasksScreen.Show), 5),
            new TargetSpec(typeof(TasksScreen), nameof(TasksScreen.Close), 0)
        };
        var resolved = new List<MethodInfo>();
        var unresolved = new List<string>();

        foreach (var specification in specifications)
        {
            var candidates = specification.DeclaringType
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(candidate =>
                    candidate.Name == specification.MethodName
                    && candidate.GetParameters().Length == specification.ParameterCount)
                .ToArray();

            if (candidates.Length != 1)
            {
                unresolved.Add(
                    $"{specification.DisplayName} parameters={specification.ParameterCount} candidates={candidates.Length}");
                continue;
            }

            resolved.Add(candidates[0]);
        }

        return new PatchTargetResolution(specifications.Length, resolved, unresolved);
    }

    private sealed class TargetSpec
    {
        public TargetSpec(Type declaringType, string methodName, int parameterCount)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
            ParameterCount = parameterCount;
        }

        public Type DeclaringType { get; }

        public string MethodName { get; }

        public int ParameterCount { get; }

        public string DisplayName => $"{DeclaringType.FullName}.{MethodName}";
    }
}

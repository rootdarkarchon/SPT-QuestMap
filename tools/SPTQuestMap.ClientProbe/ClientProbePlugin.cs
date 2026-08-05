using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.ClientProbe;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ClientProbePlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.rootdarkarchon.sptquestmap.clientprobe";
    public const string PluginName = "SPT-QuestMap M00 Client Probe";
    public const string PluginVersion = "0.0.1";

    private const int ExpectedEftPrivatePart = 40087;
    private const string ExpectedSptVersion = "4.0.13.0";
    private const string ExpectedAssemblyCSharpSha256 = "FAEF6F0B9F142F9D047495EC3DCCFD5D6974AC048368DC7045955CF54B117982";
    private const int MaximumHierarchyNodes = 750;

    private static ManualLogSource? _log;
    private Harmony? _harmony;

    private void Awake()
    {
        _log = Logger;

        if (!TryValidateEnvironment(out var identity))
        {
            Logger.LogError($"QuestMap M00 probe disabled: {identity}");
            return;
        }

        Logger.LogInfo($"QuestMap M00 probe target verified: {identity}; Unity={Application.unityVersion}");

        var questsShow = AccessTools.Method(typeof(QuestsScreen), nameof(QuestsScreen.Show));
        var tasksShow = AccessTools.Method(typeof(TasksScreen), nameof(TasksScreen.Show));
        if (questsShow is null || tasksShow is null)
        {
            Logger.LogError("QuestMap M00 probe disabled: a required Show method was not resolved.");
            return;
        }

        _harmony = new Harmony(PluginGuid);
        _harmony.Patch(questsShow, postfix: new HarmonyMethod(typeof(ClientProbePlugin), nameof(AfterQuestsScreenShow)));
        _harmony.Patch(tasksShow, postfix: new HarmonyMethod(typeof(ClientProbePlugin), nameof(AfterTasksScreenShow)));
        Logger.LogInfo("QuestMap M00 probe armed. Open a trader Tasks tab and the global Tasks screen once each.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    private static void AfterQuestsScreenShow(QuestsScreen __instance)
    {
        DumpScreen(
            "TRADER_TASKS",
            __instance,
            new Dictionary<Type, string[]>
            {
                [typeof(QuestsScreen)] = new[] { "_questsListView", "_questView" },
                [typeof(QuestsListView)] = new[]
                {
                    "_questListContainer", "_questListItemPrefab", "_questsCounterText",
                    "_toggleShowCompleted", "_toggleShowLocked"
                },
                [typeof(QuestView)] = new[]
                {
                    "_descriptionPanel", "_requirementsBlock", "_objectivesBlock", "_button", "_buttonReRoll"
                }
            });
    }

    private static void AfterTasksScreenShow(TasksScreen __instance)
    {
        DumpScreen(
            "GLOBAL_TASKS",
            __instance,
            new Dictionary<Type, string[]>
            {
                [typeof(TasksScreen)] = new[]
                {
                    "_defaultQuestsToggleSpawner", "_dailyQuestsToggleSpawner", "_notesToggleSpawner",
                    "_questItemsToggleSpawner", "_inventoryBlocker", "_notesPart", "_questItemsPart",
                    "_tasksPanel", "_questRaidGrid", "_questStashGrid", "_transferCanvasGroup",
                    "_transferButtonSpawner", "_warningImage", "_searchField", "_backButton"
                },
                [typeof(TasksPanel)] = new[]
                {
                    "_notesTaskDescription", "_questsSortPanel", "_notesTaskContent", "_scrollRect",
                    "_noActiveTasksObject", "_favoriteQuestSeparator"
                }
            });
    }

    private static void DumpScreen(string marker, Component screen, IReadOnlyDictionary<Type, string[]> fields)
    {
        var log = _log;
        if (log is null)
        {
            return;
        }

        try
        {
            log.LogInfo($"QUESTMAP_M00_BEGIN {marker} root={GetPath(screen.transform)} scene={screen.gameObject.scene.name}");
            DumpAncestorCanvases(marker, screen.transform, log);

            foreach (var entry in fields)
            {
                var owner = screen.GetComponentInChildren(entry.Key, true);
                if (owner is null)
                {
                    log.LogWarning($"QUESTMAP_M00_FIELD {marker} owner={entry.Key.FullName} result=OWNER_NOT_FOUND");
                    continue;
                }

                foreach (var fieldName in entry.Value)
                {
                    DumpField(marker, owner, entry.Key, fieldName, log);
                }
            }

            var count = 0;
            DumpTransform(marker, screen.transform, screen.transform, 0, ref count, log);
            if (count >= MaximumHierarchyNodes)
            {
                log.LogWarning($"QUESTMAP_M00_HIERARCHY {marker} truncatedAt={MaximumHierarchyNodes}");
            }

            log.LogInfo($"QUESTMAP_M00_END {marker} nodes={count}");
        }
        catch (Exception exception)
        {
            log.LogError($"QUESTMAP_M00_ERROR {marker} {exception}");
        }
    }

    private static void DumpAncestorCanvases(string marker, Transform transform, ManualLogSource log)
    {
        for (var current = transform; current is not null; current = current.parent)
        {
            var canvas = current.GetComponent<Canvas>();
            if (canvas is null)
            {
                continue;
            }

            log.LogInfo(
                $"QUESTMAP_M00_CANVAS {marker} path={GetPath(current)} renderMode={canvas.renderMode} " +
                $"overrideSorting={canvas.overrideSorting} sortingLayer={canvas.sortingLayerName} " +
                $"sortingOrder={canvas.sortingOrder} pixelPerfect={canvas.pixelPerfect}");
        }
    }

    private static void DumpField(
        string marker,
        Component owner,
        Type declaringType,
        string fieldName,
        ManualLogSource log)
    {
        var field = AccessTools.Field(declaringType, fieldName);
        if (field is null)
        {
            log.LogWarning($"QUESTMAP_M00_FIELD {marker} owner={declaringType.FullName} field={fieldName} result=FIELD_NOT_FOUND");
            return;
        }

        var value = field.GetValue(owner);
        var description = value switch
        {
            Component component => $"component={component.GetType().FullName} path={GetPath(component.transform)} active={component.gameObject.activeInHierarchy}",
            GameObject gameObject => $"gameObject path={GetPath(gameObject.transform)} active={gameObject.activeInHierarchy}",
            null => "null",
            _ => $"type={value.GetType().FullName} value={value}"
        };

        log.LogInfo($"QUESTMAP_M00_FIELD {marker} owner={declaringType.FullName} field={fieldName} {description}");
    }

    private static void DumpTransform(
        string marker,
        Transform root,
        Transform current,
        int depth,
        ref int count,
        ManualLogSource log)
    {
        if (count >= MaximumHierarchyNodes)
        {
            return;
        }

        count++;
        var components = current.GetComponents<Component>()
            .Where(component => component is not null)
            .Select(component => component.GetType().FullName ?? component.GetType().Name)
            .ToArray();

        var rect = current as RectTransform;
        var rectDescription = rect is null
            ? string.Empty
            : $" anchorMin={Format(rect.anchorMin)} anchorMax={Format(rect.anchorMax)} pivot={Format(rect.pivot)} " +
              $"anchored={Format(rect.anchoredPosition)} size={Format(rect.sizeDelta)} rect={Format(rect.rect)}";

        var canvasGroup = current.GetComponent<CanvasGroup>();
        var interactionDescription = canvasGroup is null
            ? string.Empty
            : $" canvasGroup(alpha={canvasGroup.alpha},interactable={canvasGroup.interactable},blocksRaycasts={canvasGroup.blocksRaycasts})";

        var graphic = current.GetComponent<Graphic>();
        if (graphic is not null)
        {
            interactionDescription += $" graphicRaycast={graphic.raycastTarget}";
        }

        var maskDescription = current.GetComponent<Mask>() is not null ? " mask=Mask" : string.Empty;
        if (current.GetComponent<RectMask2D>() is not null)
        {
            maskDescription += " mask=RectMask2D";
        }

        var scroll = current.GetComponent<ScrollRect>();
        var scrollDescription = scroll is null
            ? string.Empty
            : $" scroll(horizontal={scroll.horizontal},vertical={scroll.vertical},viewport={GetNullablePath(scroll.viewport)},content={GetNullablePath(scroll.content)})";

        log.LogInfo(
            $"QUESTMAP_M00_NODE {marker} depth={depth} path={GetRelativePath(root, current)} " +
            $"activeSelf={current.gameObject.activeSelf} activeHierarchy={current.gameObject.activeInHierarchy} " +
            $"sibling={current.GetSiblingIndex()} components=[{string.Join(",", components)}]" +
            rectDescription + interactionDescription + maskDescription + scrollDescription);

        for (var index = 0; index < current.childCount && count < MaximumHierarchyNodes; index++)
        {
            DumpTransform(marker, root, current.GetChild(index), depth + 1, ref count, log);
        }
    }

    private static bool TryValidateEnvironment(out string identity)
    {
        try
        {
            var executableVersion = FileVersionInfo.GetVersionInfo(BepInEx.Paths.ExecutablePath);
            var assemblyCSharpPath = Path.Combine(BepInEx.Paths.ManagedPath, "Assembly-CSharp.dll");
            var assemblyHash = ComputeSha256(assemblyCSharpPath);
            var sptCorePath = Path.Combine(BepInEx.Paths.PluginPath, "spt", "spt-core.dll");
            var sptVersion = FileVersionInfo.GetVersionInfo(sptCorePath).FileVersion ?? "missing";

            identity = $"EFT={executableVersion.ProductVersion}; privatePart={executableVersion.FilePrivatePart}; " +
                       $"SPT={sptVersion}; Assembly-CSharp={assemblyHash}";

            return executableVersion.FilePrivatePart == ExpectedEftPrivatePart
                   && string.Equals(sptVersion, ExpectedSptVersion, StringComparison.Ordinal)
                   && string.Equals(assemblyHash, ExpectedAssemblyCSharpSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            identity = $"environment check failed: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static string GetRelativePath(Transform root, Transform current)
    {
        if (ReferenceEquals(root, current))
        {
            return root.name;
        }

        var names = new Stack<string>();
        for (var item = current; item is not null && !ReferenceEquals(item, root); item = item.parent)
        {
            names.Push(item.name);
        }

        return root.name + "/" + string.Join("/", names.ToArray());
    }

    private static string GetPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var item = transform; item is not null; item = item.parent)
        {
            names.Push(item.name);
        }

        return string.Join("/", names.ToArray());
    }

    private static string GetNullablePath(Transform? transform)
    {
        return transform is null ? "null" : GetPath(transform);
    }

    private static string Format(Vector2 value) => $"({value.x:0.###},{value.y:0.###})";

    private static string Format(Rect value) => $"({value.x:0.###},{value.y:0.###},{value.width:0.###},{value.height:0.###})";
}

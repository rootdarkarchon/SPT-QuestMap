using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

/// <summary>
/// Locates the exact-version EFT QuestView prefab variants QuestMap reuses for
/// native actions, objectives, and rewards.
/// </summary>
internal static class NativeQuestViewSource
{
    private static readonly FieldInfo RewardListPrefabField = AccessTools.Field(typeof(QuestView), "_rewardListPrefab")
        ?? throw new MissingFieldException(typeof(QuestView).FullName, "_rewardListPrefab");

    public static QuestView? FindForDetails() =>
        Resources.FindObjectsOfTypeAll<QuestView>()
            .FirstOrDefault(view => view != null
                && !view.name.StartsWith("QuestMapNativeActionHost", StringComparison.Ordinal)
                && RewardListPrefabField.GetValue(view) is GameObject);

    public static GameObject? GetRewardListPrefab(QuestView view) =>
        RewardListPrefabField.GetValue(view) as GameObject;
}

/// <summary>
/// Owns a hidden EFT QuestView clone and its native transaction lifecycle.
/// </summary>
internal sealed class NativeQuestViewHost : IDisposable
{
    private readonly QuestView _view;
    private readonly ISession _session;
    private readonly InventoryController _inventoryController;
    private readonly AbstractQuestControllerClass _questController;
    private readonly ManualLogSource _log;
    private bool _bound;

    private NativeQuestViewHost(
        QuestView view,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        ManualLogSource log)
    {
        _view = view;
        _session = session;
        _inventoryController = inventoryController;
        _questController = questController;
        _log = log;
    }

    public static NativeQuestViewHost? TryCreate(
        RectTransform parent,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        ManualLogSource log)
    {
        var source = NativeQuestViewSource.FindForDetails();
        if (source is null)
        {
            log.LogWarning("QUESTMAP_M07_NATIVE unavailable=QuestView; actions and native rewards remain read-only");
            return null;
        }
        var clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
        clone.name = "QuestMapNativeActionHost";
        clone.SetActive(false);
        var view = clone.GetComponent<QuestView>();
        if (view is null)
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        return new NativeQuestViewHost(view, session, inventoryController, questController, log);
    }

    public bool Bind(QuestClass quest, string traderId)
    {
        var trader = _session.Traders.FirstOrDefault(
            value => string.Equals(value.Id, traderId, StringComparison.Ordinal));
        if (trader is null)
        {
            _log.LogWarning($"QUESTMAP_M07_NATIVE quest={quest.Id}; unavailable=trader:{traderId}");
            return false;
        }
        if (_bound) _view.Close();
        _view.Show(_session, _inventoryController, _questController, quest, trader);
        _view.gameObject.SetActive(false);
        _bound = true;
        return true;
    }

    public Task Accept(QuestClass quest) => _view.StartQuest(quest);

    public Task Complete(QuestClass quest) => _view.FinishQuest(quest);

    public Task Replace() => _view.ShowChangeQuestConfirmation();

    public void Dispose()
    {
        if (_view == null) return;
        if (_bound) _view.Close();
        UnityEngine.Object.Destroy(_view.gameObject);
    }

}

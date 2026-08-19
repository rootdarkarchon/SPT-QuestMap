using System.Collections;
using System.Reflection;
using BepInEx;
using EFT.UI;
using EFT.UI.Screens;
using SPTQuestMap.Client.Compatibility;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.Diagnostics;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Client.Patches;
using SPTQuestMap.Client.UI;
using UnityEngine;

namespace SPTQuestMap.Client;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class QuestMapClientPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.rootdarkarchon.sptquestmap.client";
    public const string PluginName = "SPT-QuestMap Client";
    public const string PluginVersion = "2.0.0";

    private PatchRegistration? _patchRegistration;
    private QuestMapDataRuntime? _dataRuntime;
    private Coroutine? _topologyWarmupCoroutine;

    private void Awake()
    {
        var configuration = QuestMapClientConfiguration.Bind(Config);
        QuestMapDebugLog.Configure(configuration.EnableDebugLogging);
        QuestGraphPalette.Configure(configuration);
        QuestMapButtonFeedback.Configure(configuration);
        var compatibility = CompatibilityValidator.Validate();
        _dataRuntime = new QuestMapDataRuntime(this, Logger, configuration);
        _patchRegistration = new PatchRegistration(PluginGuid);
        var registration = _patchRegistration.Register(compatibility, configuration, _dataRuntime);
        if (registration.Active)
        {
            QuestMapConfigurationManagerActions.Configure(
                _dataRuntime.ForceReloadTopology,
                _dataRuntime.CanForceReloadTopology);
        }
        if (registration.Active
            && (configuration.EnableTraderQuestGraph.Value || configuration.EnableGlobalTasksGraph.Value))
        {
            _topologyWarmupCoroutine = StartCoroutine(WarmTopologyAfterMainMenuReady());
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? PluginVersion;

        StartupDiagnostics.Log(Logger, assemblyVersion, configuration, compatibility, registration);
    }

    private void Update() => _dataRuntime?.UpdateRaidMonitor();

    private IEnumerator WarmTopologyAfterMainMenuReady()
    {
        while (_dataRuntime is not null)
        {
            if (CurrentScreenSingletonClass.Instance?.CheckCurrentScreen(EEftScreenType.MainMenu) == true)
            {
                // Let the completed main-menu transition settle before beginning
                // the non-blocking request without patching its shared ShowScreen path.
                yield return null;
                _dataRuntime?.WarmTopology();
                break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        _topologyWarmupCoroutine = null;
    }

    private void OnDestroy()
    {
        QuestMapConfigurationManagerActions.Clear();
        if (_topologyWarmupCoroutine is not null)
        {
            StopCoroutine(_topologyWarmupCoroutine);
            _topologyWarmupCoroutine = null;
        }

        _patchRegistration?.Dispose();
        _patchRegistration = null;
        _dataRuntime?.Dispose();
        _dataRuntime = null;
    }
}

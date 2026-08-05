using System.Reflection;
using BepInEx;
using SPTQuestMap.Client.Compatibility;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.Diagnostics;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Client.Patches;

namespace SPTQuestMap.Client;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class QuestMapClientPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.rootdarkarchon.sptquestmap.client";
    public const string PluginName = "SPT-QuestMap Client";
    public const string PluginVersion = "0.1.0";

    private PatchRegistration? _patchRegistration;
    private QuestMapDataRuntime? _dataRuntime;

    private void Awake()
    {
        var configuration = QuestMapClientConfiguration.Bind(Config);
        var compatibility = CompatibilityValidator.Validate(configuration.ForceCompatibilityFailure.Value);
        _dataRuntime = new QuestMapDataRuntime(this, Logger);
        _patchRegistration = new PatchRegistration(PluginGuid);
        var registration = _patchRegistration.Register(compatibility, configuration, _dataRuntime.ObserveQuestController);
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? PluginVersion;

        StartupDiagnostics.Log(Logger, assemblyVersion, configuration, compatibility, registration);
    }

    private void OnDestroy()
    {
        _patchRegistration?.Dispose();
        _patchRegistration = null;
        _dataRuntime?.Dispose();
        _dataRuntime = null;
    }
}

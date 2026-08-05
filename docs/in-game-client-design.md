# In-game client design investigation

This document records Milestone 0 evidence for the native SPT-QuestMap client UI. It is source-backed against the installed SPT 4.0.13 client and deliberately does not define speculative SPT 4.1 compatibility.

## Evidence boundary

- Installed binaries were inspected as metadata only. No installed SPT assembly was decompiled.
- Public client source was used as an investigation aid, then names, signatures, metadata tokens, versions, and hashes were checked against the installed assemblies.
- The public `SPT-Client-400` snapshot is commit `54e455a96ebb3ef87c4a3cc7c516409a5319e946`; it declares EFT build `0.16.9.0.40087`, matching the installed executable.
- The matched server source is commit `2891fd41fd07b6150a2192ac0d24adb93eb72862` and declares SPT `4.0.13`.
- Transform paths, concrete left/right geometry, canvas sorting, masks, and blockers were captured in-game on 2026-08-05 by the read-only M00 probe. Static serialized fields remain the durable access mechanism; transform names are recorded as evidence rather than used as guessed patch targets.

## Environment identity

| Component | Installed identity | SHA-256 |
|---|---|---|
| EFT executable | file `0.16.9.40087`; product `0.16.9.0-40087-8ce8af8a` | `58FB89AE3D36F3EDFEDD99E60362EB17271C5C10C68A4085EA37280E7911EF0A` |
| `Assembly-CSharp.dll` | assembly `0.0.0.0` | `FAEF6F0B9F142F9D047495EC3DCCFD5D6974AC048368DC7045955CF54B117982` |
| SPT client core | file/assembly `4.0.13.0`; product `4.0.13+5ca4ccff6ca1853f7d36f28562941bd069c67b6b` | `2B223747EC8E6B84947D96AAC4FF3875CBDF6E130F0D1932399CFFF8D382C4F3` |
| SPT Reflection | file/assembly `4.0.13.0` | `50B6B6B2FFC3110734FF890C6DD08DAF7814F6E37D1EDB5119F15CB87382AB5B` |
| BepInEx | `5.4.23.2` | `C65B42034BC8FFB9F0B336E416DC3884E3F99FC5A5A89EB1F2FF7868412322CD` |
| HarmonyX | BepInEx copy `2.9.0.0` | `1A21CC03424FC82C3DD1346905D16494536B9595AE4162228D99FB7C285C1031` |
| Unity | `2022.3.43f1 (85497d293fa1)` | `BF491512C0122395C4BA0316B936F22CB2D586BBF320C9417904DAC3C07CC9BE` (`UnityPlayer.dll`) |

Relevant paths:

```text
D:\Tarkov-SPT\EscapeFromTarkov.exe
D:\Tarkov-SPT\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll
D:\Tarkov-SPT\BepInEx\core\BepInEx.dll
D:\Tarkov-SPT\BepInEx\core\0Harmony.dll
D:\Tarkov-SPT\BepInEx\plugins\spt\spt-common.dll
D:\Tarkov-SPT\BepInEx\plugins\spt\spt-core.dll
D:\Tarkov-SPT\BepInEx\plugins\spt\spt-reflection.dll
```

The client is the intended SPT 4.0.13 target. BepInEx recursively loads both root DLLs and subdirectories under `BepInEx\plugins`; the production client should use an isolated `BepInEx\plugins\SPTQuestMap\` directory.

The installed environment also contains quest-UI-adjacent mods that later compatibility tests must retain: DrakiaXYZ Task List Fixes `1.7.1`, DrakiaXYZ Quest Tracker `1.6.0`, Tyfon UI Fixes `5.3.11`, and Fika Core `2.3.9`.

## Exact client targets

The following type and method tokens were read from the installed `Assembly-CSharp.dll` and match the public build-40087 source.

| Type | Type token | Important installed members |
|---|---:|---|
| `EFT.UI.QuestsScreen` | `0x02001EFA` | `Show(ISession, InventoryController, AbstractQuestControllerClass, TraderClass)` `0x0600F3CF`; `Close()` `0x0600F3D0`; fields `_questView`, `_questsListView` |
| `EFT.UI.QuestsListView` | `0x02001EF9` | `Show(...)` `0x0600F3C3`; `OnQuestSelected(QuestListItem)` `0x0600F3CA`; `Close()` `0x0600F3CD`; list container/toggle fields |
| `EFT.UI.QuestView` | `0x02001EFB` | `Show(...)` `0x0600F3D4`; `ShowChangeQuestConfirmation()` `0x0600F3DE`; `StartQuest(QuestClass)` `0x0600F3E8`; `FinishQuest(QuestClass)` `0x0600F3EB`; `Close()` `0x0600F3EE` |
| `EFT.UI.TasksScreen` | `0x02001EDE` | `Show(InventoryController, AbstractQuestControllerClass, ISession, NotesManagerClass, bool)` `0x0600F30E`; `Close()` `0x0600F318`; `OnBackButtonClick` |
| `EFT.UI.TasksPanel` | `0x02001EDC` | `Show(...)` `0x0600F2FB`; `ShowQuests(Func<QuestClass,bool>)` `0x0600F2FC`; `Close()` `0x0600F301` |
| `EFT.UI.QuestObjectivesView` | `0x02001EF2` | generic `Show(...)` `0x0600F3A5`; objective list creation `0x0600F3A6` |
| `EFT.UI.QuestObjectiveView` | `0x02001EF3` | `Show(...)` `0x0600F3AB`; native handover coroutine `method_2(QuestClass)` `0x0600F3AF`; `Close()` `0x0600F3B0` |
| `AbstractQuestControllerClass` | `0x0200217F` | abstract `AcceptQuest` `0x06010AC5`; `FinishQuest` `0x06010AC6`; `HandoverItem` `0x06010AC7`; event `OnNewQuestsAdded` |
| `QuestBookClass` | `0x0200217E` | `InitRepeatableQuests` `0x06010AAB`; `Load` `0x06010AB0`; `LoadAll` `0x06010AB1`; event `OnQuestExpired` |
| `GClass4014` | `0x0200211D` | `GetAllProfileQuestTemplates(string)` `0x06010930`; `GetAllQuestTemplates()` `0x06010931`; global/profile template stores |
| `LocalQuestControllerClass` | `0x0200219B` | concrete `AcceptQuest` `0x06010B57`; `FinishQuest` `0x06010B58`; `HandoverItem` `0x06010B59` |
| `MainMenuControllerClass` | `0x0200122E` | template/controller initialization `method_5()` `0x06009288`; `QuestController` property; `LocalQuestControllerClass` field |
| `IQuestActions` | `0x02001266` | `RequestQuestsTemplates`, `GetDailyQuests`, `QuestChange`, `QuestAccept`, `QuestComplete`, `QuestHandover` (`0x060095DA` through `0x060095E0`) |

These are version-pinned names. Obfuscated identifiers must be resolved only after the exact-version guard succeeds.

## Trader Tasks screen

`QuestsScreen.Show` first activates the vanilla screen, then calls `QuestsListView.Show` with the backend session, inventory controller, quest controller, trader, and the screen-owned `QuestView`.

The installed serialized access surface is:

```text
QuestsScreen
├── _questsListView : QuestsListView
│   ├── _questListContainer : RectTransform
│   ├── _questListItemPrefab : QuestListItem
│   ├── _questsCounterText : TMP_Text
│   ├── _toggleShowCompleted : Toggle
│   └── _toggleShowLocked : Toggle
└── _questView : QuestView
    ├── _descriptionPanel : NotesTaskDescription
    ├── _requirementsBlock : QuestRequirementsView
    ├── _objectivesBlock : QuestObjectivesView
    ├── _button : DefaultUIButton
    └── _buttonReRoll : DefaultUIButton
```

The list binds `questController.Quests` to live `QuestListItem` views, filters by `Template.TraderId`, subscribes to `OnConditionalStatusChanged`, and calls native `QuestView.Show` on selection. `Close` disposes the list and closes both child views.

Candidate patch strategy:

- Postfix `QuestsScreen.Show`, after vanilla binding and controller capture are complete.
- Preserve `_questView`; a graph selection for a live `QuestClass` can call its existing `Show` path and set `IsViewed` exactly as the list does.
- Hide only the vanilla list subtree after the graph root initializes successfully. On failure, destroy the partial graph and leave or restore the list.
- Pair ownership/cleanup with `QuestsScreen.Close` and the graph root's Unity lifecycle.

The runtime capture confirms this vanilla-Show-then-postfix strategy. `MainArea/Center` is a `1770 x 820` stretch container. Its `QuestList` child is sibling `1`, left-anchored at `554.141 x 820`; `QuestView` is sibling `0` and occupies the remaining `1347 x 820`. The safe initial graph mount is a new child under `MainArea/Center` that mirrors the `QuestList` rect and sibling placement. Only `QuestList` should be hidden after successful graph initialization; `QuestView` remains native and visible. Both list and detail panes own masked vertical scroll views, so a graph must not be inserted beneath either mask.

## Global Tasks screen

`TasksScreen` is not just a task list. Its installed fields retain the complete native feature set:

```text
TasksScreen
├── _defaultQuestsToggleSpawner / _dailyQuestsToggleSpawner
├── _notesToggleSpawner / _questItemsToggleSpawner
├── _tasksPanel : TasksPanel
│   ├── _notesTaskDescription
│   ├── _questsSortPanel
│   ├── _notesTaskContent
│   ├── _scrollRect
│   └── _favoriteQuestSeparator
├── _questItemsPart
│   ├── _questRaidGrid
│   ├── _questStashGrid
│   ├── _transferCanvasGroup / _transferButtonSpawner
│   └── _warningImage
├── _notesPart / _noteWindow / _notesContent / _addNoteButton
├── _searchField
├── _inventoryBlocker
└── _backButton -> OnBackButtonClick
```

`Awake` wires regular/daily toggles to `TasksPanel.ShowQuests`, keeps Notes and Quest Items as mutually exclusive native roots, preserves the native quest-item transfer transaction, and forwards the back button through `OnBackButtonClick`. `Close` closes both quest grids, unsubscribes quest-item selection, closes `TasksPanel` and the tooltip, then calls the base close.

Candidate patch strategy:

- Postfix `TasksScreen.Show` and retain the native roots/controller arguments.
- A future Quest Map root can occupy the `_tasksPanel` rectangle, but the Notes, Quest Items, transfer, warning, search, and back controls must not be recreated.
- Toggle replacement must preserve the native quest-item transfer and Notes CRUD/search paths.
- Any initialization failure must restore `_tasksPanel` and the original toggles/parts before returning control.

The runtime capture identifies `TasksPart` as the left/full task workspace (`1261 x 905`) and its `TasksPanel` child as the matching `1261 x 905` content surface. `NotesPart` (`550 x 904.974`) and `QuestItemsPanel` (`551 x 904.977`) are independent right-side roots. The safe graph mount is therefore a child of `TasksPart` mirroring the `TasksPanel` rect, with `TasksPanel` hidden only while the future map view is active. The existing regular/daily toggle group, Notes and Quest Items roots, grids, transfer controls, search field, blocker, and back button stay native. The task list scroll view is mask-backed and contains a UI Fixes `KeyScroller`, so replacement must occur above that subtree rather than inside it.

## Quest-template and live-state sources

### Initialization timing

`MainMenuControllerClass.method_5()` performs the following sequence before showing the main menu:

1. awaits `ISession.RequestQuestsTemplates(true)`;
2. clears and fills `GClass4014.Instance.GlobalQuestTemplates`;
3. obtains profile repeatables and registers them in the profile template dictionary;
4. creates and initializes `LocalQuestControllerClass`;
5. runs the controller;
6. awaits `QuestBookClass.InitRepeatableQuests(ISession)`;
7. completes the remaining main-menu initialization and calls `ShowMenuScreenSync()`.

Therefore both trader and global Tasks screens open after initial template and live-book initialization.

### Critical completeness finding

`RequestQuestsTemplates(true)` is a client request to `/client/quest/list`. In the matched SPT 4.0.13 server, that route calls `QuestHelper.GetClientQuests(sessionId)`, which returns:

- every quest already present in the profile;
- otherwise only quests passing faction, active-event, player-level, trader-existence, prerequisite-status, loyalty, standing, and game-edition checks.

It does **not** return the complete database topology. The installed quest database currently contains 558 templates, but a normal client template request is intentionally profile-visible rather than exhaustive. Consequently:

- `GClass4014.GlobalQuestTemplates` is not a complete locked-future source;
- `GetAllProfileQuestTemplates(profileId)` is exactly the global visible collection plus that profile's repeatable templates;
- calling `QuestBook.LoadAll()` cannot manufacture templates the client was never sent and remains forbidden for display;
- Milestone 2 needs a deliberate read-only complete-topology source. Reusing the existing server-side topology service through a small sanitized client transport is the leading option; embedding a version snapshot is a fallback, not an assumption.

### Repeatables and expiry

`QuestBook.InitRepeatableQuests` constructs `GClass4059`, subscribes `OnDailyQuestsUpdated`, and starts its update loop. `GClass4059` calls `GetDailyQuests`, emits the ranges, and delays until the earliest `UpdateTime` (minimum 30 seconds). `QuestBook.UpdateDailyQuests` registers profile templates, change requirements, and free rerolls, then loads/removes live entries. Repeatable `GClass3996.OnExpired` feeds `QuestBook.OnQuestExpired`; `GClass4005` fails or removes the live quest as appropriate and removes expired profile templates.

### Faction, event, and trader data

- Faction and seasonal/event applicability for normal templates are already enforced server-side before `/client/quest/list`; the client response does not contain a complete classification set for excluded future quests.
- `RawQuestClass.TraderId` identifies the quest trader.
- Live trader truth is `Profile.TradersInfo[traderId]`: `Unlocked`, `Disabled`, `Banned`, `Available`, `LoyaltyLevel`, `Standing`, and `SalesSum`. `Available` is `!Disabled && Unlocked && !Banned`.
- `Profile.OnTraderStandingChanged`, `Profile.OnTraderLoyaltyChanged`, and each `TraderInfo`'s change events are usable overlay-refresh signals.
- Locked-future data must remain a QuestMap-owned read-only model; it must not be injected into `QuestBookClass`.

## Native action flows

All action buttons must reuse these verified flows and retain the native `_performingAction`/selection guards.

| Action | Verified path | Observable successful result |
|---|---|---|
| Accept | `QuestView.StartQuest` -> `AbstractQuestControllerClass.AcceptQuest(quest, true)` -> `IQuestActions.QuestAccept(id, isRepeatable)` | Controller changes the live quest to `Started`; `QuestClass.OnStatusChanged` and controller conditional-status events run. `QuestView` temporarily listens to `OnNewQuestsAdded` for linked-quest messages. |
| Restart | Same `StartQuest` path when status is `FailRestartable` | Same server transaction and successful `Started` transition; no separate restart transport exists. |
| Complete | `QuestView.FinishQuest` -> controller `FinishQuest(quest, true)` -> `IQuestActions.QuestComplete(id, true, isRepeatable)` | On success the quest becomes `Success`; mutually exclusive started quests may transition to failure; native success text is shown through `ShowQuestMessage`. Rewards/profile deltas remain server-owned. |
| Repeatable reroll | `ShowChangeQuestConfirmation` -> native confirmation window -> `ISession.QuestChange(id)` | Only after a successful result, the cached repeatable is locally expired and raises its normal expiry/removal path; the repeatable update loop supplies replacement data. |
| Ordinary handover | `QuestObjectiveView.method_2` -> native eligible-item selection -> controller `HandoverItem(..., true)` -> `IQuestActions.QuestHandover` | Native item-event delta is applied; on success the task counter increments, status is rechecked, and the condition progress checker emits a change. |
| Partial-stack handover | Same handover path; selected whole stack references are sent | The matched server caps removal to the remaining required count and emits a stack change or deletion, so no custom stack split is allowed. |
| Currency handover | `QuestObjectiveView` detects `MoneyTemplateClass`, finds currency sums, and uses the same handover call | Native server quantity capping and inventory/profile delta apply. |
| Weapon assembly | `Inventory.GetWeaponAssembly(...)` selects valid assemblies, then uses the same handover call | The matched server accepts `WeaponAssembly` in the standard quest handover route and owns item removal. |

Do not invoke `IQuestActions` methods directly from new buttons when `QuestView` or `QuestObjectiveView` already owns confirmation, selection, debouncing, messages, and cleanup. Live quest actions should delegate to those controllers/views; template-only future quests remain read-only.

## Event candidates

Use event-driven overlay refresh and coalesce changes on the Unity main thread:

- `QuestClass.OnStatusChanged(QuestClass, bool)` for selected/live quest state;
- controller `OnConditionalStatusChanged` for broad quest state/condition changes;
- `AbstractQuestControllerClass.OnNewQuestsAdded(QuestClass)` for newly materialized linked quests;
- live book item-added/item-removed binding notifications;
- `QuestBookClass.OnQuestExpired(GClass3996)`;
- `GClass4059.OnDailyQuestsUpdated(IEnumerable<DailyQuestClass>, bool)`;
- `Profile.OnTraderStandingChanged` and `Profile.OnTraderLoyaltyChanged`;
- inventory/profile transaction changes already consumed by native objective views.

Do not recalculate static topology for these overlay-only events.

## Milestone 1 reference recommendation

The inert client project should target `netstandard2.1`, require `EftInstallRoot` or the shared `SPT_ROOT`, use `Private=false`, and initially reference:

```text
BepInEx\core\BepInEx.dll
BepInEx\core\0Harmony.dll
BepInEx\plugins\spt\spt-common.dll
BepInEx\plugins\spt\spt-reflection.dll
EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll
EscapeFromTarkov_Data\Managed\Comfort.dll
EscapeFromTarkov_Data\Managed\Sirenix.Serialization.dll
EscapeFromTarkov_Data\Managed\UnityEngine.dll
EscapeFromTarkov_Data\Managed\UnityEngine.CoreModule.dll
EscapeFromTarkov_Data\Managed\UnityEngine.UI.dll
EscapeFromTarkov_Data\Managed\UnityEngine.UIModule.dll
EscapeFromTarkov_Data\Managed\Unity.TextMeshPro.dll
EscapeFromTarkov_Data\Managed\UnityEngine.TextRenderingModule.dll
```

The compatibility guard should verify all of:

- EFT executable private build part `40087`;
- SPT client core file version `4.0.13.0`;
- `Assembly-CSharp.dll` SHA-256 `FAEF6F0B9F142F9D047495EC3DCCFD5D6974AC048368DC7045955CF54B117982`;
- resolution of the exact patch candidates before enabling any feature.

The current public Task List Fixes `master` targets later SPT/EFT versions; it is useful only as a project/pattern reference. Do not copy its current dependency or version constant into this target.

## Runtime probe and M00 evidence

The read-only probe project is `tools/SPTQuestMap.ClientProbe`. It validates the identities above before installing postfixes on the two `Show` methods. It records only transform/component/rect/canvas/mask/raycast and known serialized-field paths.

Build:

```powershell
dotnet build .\tools\SPTQuestMap.ClientProbe\SPTQuestMap.ClientProbe.csproj -c Release -p:EftInstallRoot="D:\Tarkov-SPT"
```

The tested DLL was temporarily placed under `BepInEx\plugins\SPTQuestMap-M00-Probe\` and exercised as follows:

1. Start SPT/EFT normally. No disposable profile or profile backup is required because no action is performed.
2. Open one trader and its Tasks tab. Wait until the quest list and native details are visible.
3. Open the global Tasks screen once; briefly switch through regular tasks, daily tasks, Quest Items, and Notes without moving items or editing notes.
4. Exit EFT normally.
5. Inspect `BepInEx\LogOutput.log` for paired `QUESTMAP_M00_BEGIN/END TRADER_TASKS` and `GLOBAL_TASKS` blocks.

The 2026-08-05 `D:\Tarkov-SPT\BepInEx\LogOutput.log` capture contains:

- six paired trader `QUESTMAP_M00_BEGIN/END` captures and one paired global capture;
- all required `QUESTMAP_M00_FIELD` entries resolved;
- separate trader list/detail and global task/notes/items roots, including their concrete rectangles;
- `ScreenSpaceOverlay`, default-layer, sorting-order-zero ancestor canvases for both surfaces;
- mask, scroll-content, graphic-raycast, canvas-group, transfer, and blocker state;
- normal close/back navigation and no `QUESTMAP_M00_ERROR`, exception, or patch failure.

The global hierarchy and the final trader hierarchy reached the probe's intentional 750-node diagnostic cap, but all required serialized fields and insertion-relevant roots were captured before the cap. Earlier trader captures completed at 420-605 nodes. The warnings are therefore diagnostic limits, not missing acceptance evidence.

The only errors after probe load were unrelated existing-mod messages from ModGod Client Enforcer, Weapon Camo and Stickers, and Fika. ModGod correctly reported the temporary probe DLL as an extra file; removal after capture resolves that expected condition. No native quest or inventory action was executed.

Milestone 0 is complete. The deployed probe was removed after evidence review; its source remains in `tools/SPTQuestMap.ClientProbe` as a reproducible, exact-version-gated investigation artifact.

## Milestone 1 client compatibility harness

The production client project is `src/SPTQuestMap.Client/SPTQuestMap.Client.csproj`. It targets `netstandard2.1` and accepts either the `EftInstallRoot` MSBuild property or the shared `SPT_ROOT` environment property. Both point to the Tarkov directory containing `EscapeFromTarkov.exe`, `BepInEx`, and the `SPT` subdirectory. Build-time guards require the exact client executable layout, each referenced assembly, and `BepInEx/plugins/spt/spt-core.dll`; a missing root fails before reference resolution with an actionable path-specific error.

The final Milestone 1 compile references are deliberately limited to the assemblies used by the inert harness:

```text
BepInEx/core/BepInEx.dll
BepInEx/core/0Harmony.dll
EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll
EscapeFromTarkov_Data/Managed/Sirenix.Serialization.dll
EscapeFromTarkov_Data/Managed/UnityEngine.dll
EscapeFromTarkov_Data/Managed/UnityEngine.CoreModule.dll
```

Every installed reference has `Private=false`. `spt-core.dll` is verified as a required runtime file and read for its file version, but it is not a compile reference because the harness does not consume an SPT client type. The release output therefore contains only `SPTQuestMap.Client.dll`, its PDB, and the SDK-generated dependency manifest; no game, SPT, BepInEx, Harmony, Sirenix, or Unity assembly is copied.

Startup binds these default-false settings beneath the plugin GUID `com.rootdarkarchon.sptquestmap.client`:

```text
Features.EnableTraderQuestGraph = false
Features.EnableGlobalTasksGraph = false
Features.EnableCustomQuestDetails = false
Diagnostics.EnableDebugLogging = false
Diagnostics.ForceCompatibilityFailure = false
```

`ForceCompatibilityFailure` exists only to exercise the safe-disable branch without changing an installed binary. Feature toggles are placeholders in Milestone 1. If any replacement feature is requested, the harness logs a safe disable and still installs no patches.

Compatibility validation reuses the proven M00 identity boundary: EFT executable private build part `40087`, SPT client core file version `4.0.13.0`, and the exact `Assembly-CSharp.dll` SHA-256. Only after those checks pass does the harness resolve the four initial screen lifecycle candidates as unique declared methods by declaring type, method name, and expected parameter count:

```text
EFT.UI.QuestsScreen.Show   parameters=4
EFT.UI.QuestsScreen.Close  parameters=0
EFT.UI.TasksScreen.Show    parameters=5
EFT.UI.TasksScreen.Close   parameters=0
```

The raw installed tokens recorded in Milestone 0 remain valuable on-disk identity evidence, but they are not runtime patch keys. The 2026-08-05 default-config capture proved that BepInEx preloader patching changes the loaded module's metadata layout: the executable/SPT/hash checks all passed while token-equality matching returned `0/4`. The corrected resolver follows the M00 probe's type/name approach, adds an arity check, and requires exactly one candidate. Exact compatibility continues to come from the raw DLL hash before any source-sensitive type is resolved.

The patch-registration lifecycle creates an owned Harmony instance only for an exact compatible environment with all replacement features disabled. It registers zero prefix, postfix, transpiler, or finalizer methods. Disposal calls `UnpatchSelf` as future-safe cleanup, but Milestone 1 has nothing to remove. An incompatible identity, unresolved target, forced failure, or prematurely enabled feature remains vanilla and reports `active=false; safelyDisabled=true`.

Startup emits one structured block (`QUESTMAP_M01_STARTUP`, `ENVIRONMENT`, `TARGETS`, `CONFIG`, and `STATE`). It includes plugin/support/detected versions, `Assembly-CSharp` identity and hash, resolved and unresolved target names, installed patch count, configuration, and the active/safe-disable result. No update-loop or per-frame logger exists.

Build:

```powershell
dotnet build .\src\SPTQuestMap.Client\SPTQuestMap.Client.csproj -c Release -p:EftInstallRoot="D:\Tarkov-SPT" -m:1
```

The current static validation passes with zero warnings and errors. The missing-root guard was separately exercised against a nonexistent directory and failed with the intended `EftInstallRoot does not contain EscapeFromTarkov.exe` error. The server/browser suite remains 92/92. The tested client DLL and PDB were deployed alone to `BepInEx/plugins/SPTQuestMap/`.

Milestone 1 runtime validation completed on 2026-08-05 using separate archived logs because EFT clears `LogOutput.log` on restart. The corrected default-config run loaded the plugin, detected the exact EFT/SPT/hash identity, resolved `4/4` lifecycle targets with no unresolved names, reported every replacement feature `False`, installed zero patches, and entered the compatible inert state. Manual inspection confirmed both the trader Tasks tab and global Tasks screen remained vanilla. The forced-failure run left all feature flags false, skipped source-sensitive resolution, installed zero patches, and reported `compatible=False`, `active=False`, and `safelyDisabled=True`. The diagnostic setting was restored to `false` afterward. Other log errors belonged to pre-existing mods; no QuestMap exception or patch failure occurred.

Milestone 1 is complete. Milestone 2 may begin from `M02-graph-model-client-data-adapter.md`.

## Milestone 2 graph model and read-only data boundary

Milestone 2 uses a three-part boundary:

```text
SPT server database topology + sanitized session repeatable definitions
        -> /questmap/client/topology (read-only native SPT route)
        -> SPTQuestMap.Core immutable topology + deterministic layout

live QuestBookClass enumerable + EFT.Profile trader/player state
        -> read-only EftLiveSnapshotAdapter
        -> replaceable profile overlay keyed by quest ID
```

The complete-future source is deliberately server-owned. `/client/quest/list` remains the game's profile-visible response and is not repurposed. `QuestMapDataService.GetTopology()` already builds the complete localized database graph for the accepted browser application, so `QuestMapClientStaticRouter` returns that sanitized DTO at `/questmap/client/topology`. For the route's authenticated session it also adds sanitized profile-generated repeatable node definitions; no raw profile, inventory, controller, or write operation is exposed.

The client calls the exact installed SPT 4.0.13 public-static surface `SPT.Common.Http.RequestHandler.GetDataAsync(string)`. `spt-common.dll` and `Newtonsoft.Json.dll` are exact external compile references with `Private=false`. The raw JSON is mapped into `SPTQuestMap.Core`; vendor DTO types and services do not cross into the core.

`SPTQuestMap.Core` targets `netstandard2.1` and has no game/framework integration dependency. Its topology owns stable ordinal lookups for node by quest ID, incoming edges by target, outgoing edges by source, and trader by ID. Edges with missing predecessors or targets remain in the edge collection and are separately diagnosed. Unsupported start conditions are transported as explicit entries containing stage, condition type, and condition ID. Unsupported objective condition types retain their source condition name in the objective definition.

The initial layout is pure and deterministic. A Kahn pass derives horizontal prerequisite ranks for the acyclic portion; stable quest/trader/name/ID ordering determines rows. Nodes remaining after the pass use a bounded deterministic cycle fallback. Fixed dimensions are `320 x 112`, with a `96` layer gap and `28` row gap. No profile status participates in layout.

The live adapter reads only these verified exact-client surfaces:

- `QuestClass.Id`, `QuestStatus`, `IsVisible`, `Conditions`, `CompletedConditions`, and `ProgressCheckers`;
- `Condition.id`, `value`, `index`, and `ConditionProgressChecker.CurrentValue`;
- `GClass3996.ExpirationDate` for repeatables;
- `Profile.ProfileId`, `Side`, `Info.Level`, and `TradersInfo`;
- `Profile.TraderInfo.Available`, `LoyaltyLevel`, `Standing`, and `SalesSum`.

It never calls `QuestBookClass.LoadAll()`, `AddTemplates`, `RemoveQuestTemplate`, `SetQuestStatusData`, or any quest/profile/inventory controller mutation. The live book is only consumed through its existing `IEnumerable<QuestClass>` surface.

Invalidation rules are explicit:

- rebuild topology and layout only when the server topology version or profile-generated template set changes;
- rebuild the live overlay for quest status/condition, trader standing/loyalty/availability, player-level, repeatable-expiry, or relevant inventory-driven progress changes;
- never rebuild layout for an ordinary overlay refresh;
- selection/highlight and viewport remain later independent render-state layers.

The implementation is attached through observation-only postfix hooks on the verified trader and global Tasks `Show` methods. It does not replace either screen. Runtime validation normalized 561 nodes and 736 edges, overlaid 25 live quests, preserved the quest-book count at 25 without calling `LoadAll` or injecting templates, and reused the same topology and layout across repeated refreshes. Both native screens remained vanilla and no QuestMap runtime error occurred.

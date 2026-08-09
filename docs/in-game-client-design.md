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
Diagnostics.EnableDebugLogging = false
Diagnostics.ForceCompatibilityFailure = false
```

`ForceCompatibilityFailure` exists only to exercise the safe-disable branch without changing an installed binary. The two replacement toggles were placeholders in Milestone 1; the completed implementation makes QuestMap details intrinsic to either replacement rather than exposing a third independent toggle.

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

## Milestone 3 trader graph lifecycle

The trader vertical slice keeps vanilla initialization authoritative:

```text
QuestsScreen.Show(...)
    -> vanilla QuestsListView.Show(...) and QuestView setup
    -> QuestMap postfix captures the exact live arguments
    -> M02 topology/overlay becomes ready
    -> build graph and read-only future detail roots
    -> hide only _questsListView after both roots succeed

QuestsScreen.Close()
    -> QuestMap prefix disposes owned roots and restores vanilla objects
    -> vanilla Close performs its normal child cleanup and navigation
```

The mounted graph is a sibling of the verified `MainArea/Center/QuestList` root and copies that root's `RectTransform`. It does not mount inside the list mask and does not cover `MainArea/Center/QuestView`. The future-detail root separately mirrors the native quest-view rectangle and is active only for a topology-only selection.

`TraderGraphProjectionBuilder` derives a read-only view from the stable M02 topology and layout. It filters nodes by exact `TraderId`, removes every edge with a hidden endpoint, and compacts only the retained rank coordinates. It does not rebuild or mutate the global topology/layout. External prerequisites therefore do not draw across the trader filter but remain available as related IDs in read-only details.

The milestone renderer creates one node button per visible quest and one `QuestGraphEdgeGraphic` for all visible edges. The edge component emits every connection into one `VertexHelper` mesh, preserving the shared-contract ban on one GameObject per edge. A viewport mask plus one input handler owns drag panning and wheel zoom; fit-to-visible changes only the content transform.

After the first visual pass, the M03 batched edge mesh was refined without changing its ownership model. Each source and target receives stable vertically distributed ports; forward edges use cubic Bézier curves, backward/cyclic edges use deterministic upper lanes, and arrowheads expose direction. This is the M03 readability floor, not the final router. Crossing minimization, obstacle-aware lanes, layout refinement, and production performance remain Milestone 5 scope.

Mouse-wheel zoom must preserve the graph-space point beneath the cursor. The viewport-local pointer returned by `RectTransformUtility` is pivot-relative, while graph `anchoredPosition` is relative to the viewport's top-left anchor. The handler explicitly converts the pointer with `viewportLocal - (rect.xMin, rect.yMax)` before applying the scale/position transform; mixing these spaces produces the rejected center-biased behavior.

Live selection resolves an existing `QuestClass` from `questController.Quests` by ID. It calls `QuestView.Close()` before every new live binding to dispose the prior native subscriptions, sets the existing quest's `IsViewed` flag, and invokes the exact installed method:

```text
QuestView.Show(ISession, InventoryController, AbstractQuestControllerClass, QuestClass, TraderClass)
```

QuestMap does not copy native buttons or invoke quest transports. A topology-only selection closes and hides the native view and shows an action-free QuestMap pane containing the template description, effective requirements, ordered objectives, and related quest IDs. It never fabricates a live quest.

The runtime stores at most one owned controller per `QuestsScreen`. A repeated `Show` disposes the prior controller before mounting another; a pending async topology load is canceled logically when the screen closes. Feature disable and the forced-initialization diagnostic never hide the vanilla list. Initialization failures destroy partial roots, reactivate the list and native detail, and emit structured `QUESTMAP_M03_ERROR`/`STATE` diagnostics with screen and trader context.

The verified M03 list-area mount is deliberately transitional. User review requires the final trader graph to occupy the complete Tasks pane, with selected details presented in a narrower side panel comparable to the browser QuestMap. That composition belongs to Milestone 7: it requires the QuestMap-owned detail pane while continuing to delegate live actions to the native controllers. M03 must not prematurely resize or clone the native `QuestView` merely to approximate that final layout.

## Milestone 4 native actions and reactive invalidation

Milestone 4 retains the exact native action paths documented above. QuestMap creates no accept, restart, handover, completion, or reroll button and never calls `AcceptQuest`, `FinishQuest`, `HandoverItem`, `QuestChange`, or an `IQuestActions` transport. Selecting a live graph node continues to bind the existing `QuestClass` to the native `QuestView`, so EFT owns confirmation, busy state, eligible-item selection, server response handling, inventory/profile deltas, rewards, messages, and duplicate-submission prevention.

The exact installed build-40087 metadata exposes the following public event boundary, subscribed only after the existing EFT/SPT/hash compatibility guard passes:

- `QuestClass.OnStatusChanged(QuestClass, bool)` and `OnConditionChanged(QuestClass)`;
- `ConditionProgressChecker.OnConditionChanged`, `OnReset`, and `OnDisconnect`;
- controller `OnConditionalStatusChanged` and `OnNewQuestsAdded`;
- quest-book `ItemAdded`, `ItemRemoved`, `ItemsAdded`, `ItemsRemoved`, `ItemUpdated`, `AllItemsRemoved`, and `OnQuestExpired`;
- profile `OnTraderStandingChanged` and `OnTraderLoyaltyChanged`, plus per-trader availability, standing, loyalty, and sales-sum changes;
- `InventoryController.OnProfileUpdate` while a trader screen owns that controller.

One `ReactiveQuestMonitor` is owned by `QuestMapDataRuntime` in menu contexts. It deduplicates quest, progress-checker, trader, and inventory subscriptions by reference, resynchronizes after book/template changes, and detaches everything when the controller changes or the plugin disposes. Individual signals are coalesced for one Unity frame. In raid, those broad subscriptions are replaced by `RaidQuestProgressMonitor`: it listens only to active objective checkers and quest-status/book lifecycle events, compares each signaled checker's `CurrentValue` before invalidating, and preserves the exact objective ID. A permanent 100 ms fallback poll rotates through at most 16 cached checker values per tick rather than hashing every active quest's full `Progress` tree. It starts when the live `GameWorld`/`AbstractGame` context exposes the local player's exact 4.0.13 `AbstractQuestControllerClass`, remains active even when no QuestMap UI is open, and stops when the raid context disappears. `QUESTMAP_M06_RAID_PERF` reports cumulative checker count, polling time, and overlay timing every 30 seconds and at raid end.

Raid refreshes never request the sanitized server profile projection: the server-side profile can remain stale until raid completion. Static topology, applicability, route membership, asset metadata, and server-derived availability gates remain server-owned; exact live EFT status and objective progress override only dynamic state/progress presentation. A menu Tasks-screen open resumes the normal server projection refresh. This hybrid boundary prevents stale server data from reverting live `Started`, `AvailableForFinish`, completion/failure, or objective progress while preserving server authority for Lightkeeper/Ref/Jaeger availability and other future gates.

QuestMap owns a permanent raid-progression notification feature on top of that change stream. The current policy notifies for every quest whose live status or objective progress changes. When one action mutates both an aggregate condition and its child, the most specific changed leaf objective is selected so the card names the task the player actually advanced. One compact top-right card is reused for the entire raid: it shows quest artwork, trader portrait, quest and changed-task names, and places capped current/required values on the objective progress bar when numerical progress exists. It omits the redundant status label. A newer update replaces the card content and restarts its display/fade lifetime instead of stacking another card. Overall card opacity is configurable through `Notifications.RaidNotificationOpacity`. The change contract includes the quest ID and objective ID so a later per-quest preference layer can filter notifications without replacing the monitor or renderer.

Invalidation remains tiered:

```text
ordinary status/objective/trader/inventory event
    -> replace profile overlay
    -> update existing node colors and labels in place
    -> preserve topology, layout, graph objects, viewport, selection

repeatable expiry/reroll or live template absent from topology
    -> reload sanitized topology route
    -> rebuild deterministic layout and current trader projection once
    -> preserve selection if its node remains; otherwise clear it
```

Topology invalidation is driven by the exact quest ID carried by new/add events and by explicit repeatable expiry/removal signals. It must not be inferred from the existence of any live quest absent from the sanitized topology: another installed mod can contribute a stable unmatched live quest, and treating that unrelated baseline mismatch as a change would rebuild layout and reset the viewport after every ordinary action.

When a legitimate topology invalidation replaces the trader projection, the controller captures the outgoing graph content scale and anchored position and applies that exact viewport transform to the replacement. Topology and layout may change beneath the viewport, but neither zoom nor pan changes implicitly; fit remains an explicit user action.

The native detail view already owns its live subscriptions, so an ordinary overlay refresh does not close or rebind it during an action. A selected live quest remains selected through accept, partial handover, and completion. A future node that materializes becomes a native selection without stealing any other selection. If an expired/rerolled live quest disappears, selection is cleared and no stale native binding remains. Newly unlocked quests update visually but never take selection.

The M04 screenshots corrected an earlier hierarchy assumption: the complete `QuestsListView` rectangle also contains the native accept/replace strip and completed/locked controls above its scrolling list. Hiding or covering that complete root makes native actions inaccessible regardless of sibling order. The controller now resolves the exact serialized `_questListContainer`, finds its owning `ScrollRect`, keeps the complete `QuestsListView` active, disables the scroll component, hides only its viewport, and mounts the graph as that viewport's replacement. Disposal restores the viewport's prior active state and the scroll component's prior enabled state. The graph is additionally kept behind the native detail whenever they share a parent. The read-only future pane calls `SetAsLastSibling` only while shown, after the native detail has been intentionally closed and hidden.

With debug logging enabled, each coalesced batch emits exact old/new quest statuses and one `QUESTMAP_M04_REFRESH` record containing reasons, changed quest count, topology/layout identity, invalidation category, and selection consequence. Normal configuration does not emit event-level diagnostics.

## Milestone 5 production renderer and shared core

The M03 view was replaced at the screen-controller boundary by reusable `QuestGraphView`, which depends on `IQuestGraphProjection` rather than trader-screen types. The trader adapter still mounts it into the exact M04-validated vanilla scroll viewport and retains the native control/detail ownership; M05 does not expand into the M07 full-pane detail redesign.

Node GameObjects are now a bounded viewport pool. A runtime-neutral spatial index returns only nodes intersecting the padded graph-space viewport, and the pool rebinds static content only when a node enters that set. Overlay refreshes update the active node status layer in place. Selection is a separate pure-core set containing the selected quest, every recursive prerequisite, and direct successors.

Edges use deterministic pure-core port/route calculations and fixed-size Unity `MaskableGraphic` batches. Selection rebuilds only the relevant batched mesh style: prerequisite and direct-successor paths brighten while unrelated edges dim. Pan, zoom, and resize never regenerate route geometry.

Unity `PlayerPrefs` stores scale, anchored position, and selected quest under a hashed scope containing topology version, profile ID, and view type. Screen reopen restores the matching state; explicit Fit remains the only reset, Center Selected retains zoom, and a legitimate topology rebuild preserves the live transform while moving persistence into the new topology scope.

## Milestone 6 global Tasks ownership

The global replacement retains vanilla `TasksScreen.Show` as the initialization authority. Its delayed postfix resolves the exact installed `_tasksPanel`, regular/daily toggle spawners, Notes/Quest Items roots, and search field before changing any active state. The existing regular/daily toggle objects are relabeled as In Progress and Quest Map; their original vanilla callbacks still run first, and QuestMap's later listener closes the native task panel before presenting the graph. Notes and Quest Items keep their original callbacks, components, grids, transactions, warnings, blockers, and back navigation.

### Native task-list mod compatibility

The global and trader Tasks tables are QuestMap-owned renderers, not subclasses or decorated instances of EFT's native task-list rows. Third-party mods whose integration point is the native list—specifically the reviewed SPT 4.0-compatible Quest Tracker 1.6.0 and Task List Fixes—cannot inject their row controls, tracked-state presentation, layout adjustments, or list patches into QuestMap's table. This is an intentional replacement boundary rather than a missing compatibility hook.

QuestMap supplies the replacement functionality it needs directly: profile-scoped manual and native-favorite tracking, map-aware implicit tracking, in-raid progress notifications, a tracked-quest overlay, corrected objective presentation, sorting, filtering, pinning, and native EFT action bridges. Users should not expect Quest Tracker or Task List Fixes UI changes to appear while a QuestMap Tasks replacement is active. Disabling the relevant QuestMap replacement restores the complete native screen and therefore restores the native integration surface for those mods.

The graph root spans the union of the lower Tasks, Quest Items, and Notes content regions rather than inheriting the narrower native task-list column. It is ordered below the native Quest Items and Notes branches, so those right-side panels behave as toggleable overlays and do not hide or resize the graph. Both overlay toggles permit switch-off, start unselected, and directly preserve their native roots. Closing the inactive `TasksPanel` and disabling its separate `_notesTaskDescription` root are both required: the latter owns `NotesTaskDescriptionShort._image`, `_loader`, and `_description`, which can otherwise outlive the list and render stale localized content over the graph.

The native client feed now transports the server/browser state's sanitized `DefaultVisibleQuestIds` and `AllApplicableQuestIds`, plus the topology's Collector and Lightkeeper path sets. Repeatable nodes are added explicitly to both visibility sets. `GlobalQuestGraphProjectionBuilder` owns only presentation filtering and rank compaction; it does not reproduce SPT faction, event, prerequisite, loyalty, standing, or game-edition availability.

Two renderer scopes are independent:

```text
global:InProgress
global:Full
```

Mode, future depth, finished visibility, trader, search, and focus settings persist per active profile. Viewport and selection remain scoped by topology version, profile, and view. Ordinary overlay changes update pooled nodes in place when membership is stable; active/finished membership changes rebuild only the projection and preserve the live transform. In full-map mode, ordinary selection unions the selected quest's complete applicable predecessor closure after presentation filters have run, then rebuilds the projection without resetting the viewport. Collector and Lightkeeper are non-filtering split card strips. The Finished control is active when finished quests are visible.

The shared card visual now consumes the already-sanitized quest image and trader portrait URLs through SPT's authenticated client request handler. Sprites are requested lazily by pooled visible cards, cached for the graph lifetime, and destroyed with the graph root; missing assets retain the initial fallback. Quest art uses uniform cover scaling against the complete card width and is clipped to the card bounds, so its aspect ratio is preserved while vertical overflow is cropped. Trader portraits retain contain scaling. The status rail remains readable above the imagery, and Collector/Lightkeeper strips share the lower edge. In Progress uses a dense three-column task-card projection ordered by trader/name rather than dependency ranks.

The first MS6 slice exposes a compact selection summary rather than implementing the MS7 custom detail pane. An explicit Native Tasks control temporarily calls the original `TasksPanel.Show(...)` and reveals that panel, with a separately owned Return to Map button; returning closes the panel before restoring the graph. Existing native task interactions therefore remain reachable without leaving their lifecycle running behind the graph. QuestMap does not invoke a quest or inventory transport from this screen.

Any mount error occurs before `_tasksPanel` is hidden. Cleanup removes added listeners, restores the original toggle labels and task root, and destroys only QuestMap-owned roots. `TasksScreen.Close` performs the same cleanup before vanilla closes its grids, panel, tooltip, and base screen.

### Milestone 6 architecture correction

The relabeled regular/daily native toggles, modified native Notes/Quest Items toggle-group behavior, and Native Tasks/Return to Map escape hatch above describe the current transitional implementation, not the accepted final M06 composition.

The target is one QuestMap-owned overlay spanning the complete usable Tasks surface. It owns custom In Progress, Quest Map, Quest Items, and Notes controls. The original regular-task and operational-task selectors and list roots remain unmodified and hidden underneath; QuestMap does not need a route back to them while enabled. Disabling the replacement in plugin settings, or an automatic safe fallback after failure, restores the complete vanilla screen.

The custom Quest Items and Notes controls do not recreate native content. They drive the verified native state/root, raise the relevant real UI branch above the QuestMap overlay, explicitly activate it, and hide the other side branch. Reselecting the active custom control closes that branch. Native grids, transfer controllers, warnings, Notes CRUD/search, blockers, and cleanup remain EFT-owned.

While QuestMap owns the global surface, both fixed-width native side roots are temporarily bounded to the QuestMap canvas height and aligned to its right edge. The separately owned native Notes search is placed just inside the same top-right canvas boundary so it cannot cover QuestMap's header controls. A QuestMap-owned non-raycasting gradient is inserted behind each translucent native panel; original native transforms are captured before adjustment and restored on disposal.

Profile-generated repeatables use the web composition rather than ordinary dependency ranks: a dedicated band above the static graph, split into Daily then Weekly groups and ordered by trader, with status, progress, handover-ready state, and remaining/expired time. The band shares selection/filter ownership but does not participate in static focus/layout unless a source-backed dependency exists.

The server-produced applicable sets remain authoritative, but M06 must now prove explicit in-game/web parity for faction-only quests, active and inactive seasonal quests, `None` quests, and descendants removed behind excluded event ancestry. Selection, Focus, trader/search/level filters, and Show All Future may only operate inside that applicable set.

Parity applies to the complete visible-set calculation, not only applicability. Given equivalent topology/profile state and filter inputs, web and in-game must produce identical ordinary visible IDs and contextual prerequisite/successor additions for future depth, finished visibility, level eligibility, trader context, search, selection, Focus, and combinations of those filters. The client should consume a shared pure projection contract rather than maintain a simpler divergent copy of `QuestMapPageState.Recalculate`.

The only omitted in-game filter is the web setting that hides the Daily/Weekly band. In-game treats that setting as always enabled: matching operational quests remain visible in their dedicated band while still obeying search, trader, level, finished/status, applicability, and other relevant shared rules.

The global graph is intentionally read-only during M06. M07 owns its custom selected-quest detail/action bridge; plugin disable is the interim way to restore native task actions. The transitional Native Tasks escape hatch must be removed rather than polished.

The implemented global chrome follows the accepted web hierarchy within EFT's surface. Quest Map and In Progress are view tabs in the first row; Notes and Quest Items are right-bound custom controls. Search plus Future/Finished/level filters form the left tool group below, followed by a direct All/trader portrait strip in the established web trader order. Center, zoom, Fit, Focus Chain, and Clear Selection live in a compact overlay inside the canvas. The old title label and single horizontal button train are not retained.

Native side-overlay ownership is explicit rather than inferred from native root activity. A `None`/`Notes`/`QuestItems` state invokes the corresponding real EFT toggle callback for lifecycle behavior, then settles both hidden native toggles and both real roots to the requested mutually exclusive state. Re-selecting the active custom control closes it. The graph is the top workspace branch by default; only the active native side branch is raised above it.

Card state is now evaluated from the active profile overlay with the same priority as the web UI. Exact live terminal states take precedence, followed by trader availability, pending time, current level, trader loyalty/standing, prerequisites, and availability. The level and trader-context filters consume that same evaluated state, preventing static inherited requirements from mislabeling a high-enough-level prerequisite-gated quest. Status, relationship-edge, Collector, Lightkeeper, completion, and terminal colors use the web palette; completed and end-of-line markers and the swatch legend use the same meanings. Detailed blocker text remains M07 work.

The current In Progress content layout remains intentionally unchanged after the 2026-08-06 parity correction because the user has a separate composition proposal. It remains an open M06 runtime/usability item rather than being silently accepted or moved to M07.

As of the 2026-08-07 correction, the native feed also carries the browser profile builder's exact sanitized display state per quest, server progress percentage, repeatable end time, and direct unmet-prerequisite IDs. `TraderAvailabilityEvaluator` therefore remains authoritative for Lightkeeper, Ref, Jaeger, and other unlock-sensitive traders. Reactive quest signals refresh this server profile projection; static topology and deterministic layout are replaced only when the normalized topology version changes. The normal web view and native full-map projection call the same pure-core visible-set calculation for future depth, finished/level/trader/search filters, selection context, Focus, direct trader successors, and unmet prerequisites. Browser comparison remains its intentional two-profile wrapper. Local gate reconstruction exists only as a compatibility fallback for a feed without authoritative states.

Global selection dispatch deliberately separates single and double clicks. A single click is delayed briefly so a double click can set selection and Focus in one projection rebuild. Reselecting the current quest does nothing; a new selection rebuilds only when contextual membership changes. A background click in Focus clears Focus but retains selection, and only an ordinary later background click clears that selection. In Progress is the migrated default/left tab. Quest artwork uses aspect-preserving longest-edge fit, the All trader choice loads the web `unknown.png`, and search has a dedicated reset action.

In Progress now crosses a renderer boundary after projection. `InProgressQuestTableView` consumes the active projection as a vertically scrolling row set; it does not instantiate the edge layer, spatial index, node pool, or pan/zoom handler. The global controller talks to both renderers through `IGlobalTasksContentView`, allowing selection, reactive overlay refresh, asset caching, viewport persistence, native-overlay bounds, and disposal to remain shared without pretending the table is graph geometry. Pure-core `InProgressQuestTableSorter` owns the Daily/Weekly/ordinary section order, inactive Trader/Location/Quest order, and ordered multi-key comparison contract.

The In Progress filter chrome is faceted: the map strip excludes locations with no quests after the non-location filters, and the trader strip excludes traders with no quests after the non-trader filters. When mounted during a raid, the default trader selection is All and the default enabled location set is Any, Transit (`marathon`), and the current map, including the exact Factory-night/day and Ground-Zero-high/base aliases used by the client quest system. Every default location is still an ordinary independent toggle.

The table reserves the scrollbar width in both its fixed header and row viewport so separators share one coordinate system. Quest/task expansion state is controller-owned for the Tasks-screen lifetime; the completed-task visibility setting is profile-persisted. `WidthDrivenAspectImage` derives image height exclusively from current column width and source aspect ratio on layout changes, keeping quest and location artwork centered without height-driven distortion.

Quest pinning reuses EFT's exact Tasks-panel favorite service rather than creating QuestMap state. The pin set is not part of `characters.pmc.Quests`; SPT persists it profile-scoped in `SPT/user/sptRegistry/registry.json` as `favorite_quests_<profileId>`. The exact-build `TasksPanel.gclass3794_0` service owns `IsFavorite` and `ToggleFavorite`, including native persistence and change notification. In Progress adds a narrow star-button column and partitions already-filtered rows into Pinned, Daily, Weekly, and ordinary sections. Pinned rows appear once in the first section and retain the active multi-key sort order across ordinary and repeatable quests.

The repeatable surface no longer owns an opaque gray band. Daily and Weekly are independent left-to-right groups with per-group remaining time and underlines, followed by one separator before ordinary quests. The full QuestMap surface takes the resolved parent's complete width. Its top is raised from the conservative native-control union to the measured 44-pixel Character-navigation boundary, reclaiming the otherwise-empty native subheader strip. A 40-pixel minimum top-inset guard fails back to vanilla if hierarchy drift would let the overlay cover Gear, Health, Skills, Map, Tasks, Achievements, or Prestige.

Raid objective and ordinary quest-status signals use a dedicated per-quest patch boundary. The adapter captures only the signaled `QuestClass`, replaces only its cached `QuestLiveState`, and preserves the existing profile overlay, topology, layout, applicable sets, and server-authoritative static metadata. Mounted graph views update only the matching active card; the In Progress table rebuilds only the matching row and reflows rows only when its height or a status/progress sort key can change ordering. Quest-book membership changes remain on the broader refresh path because they can add/remove generated quests and alter projection membership.

All client artwork requests share one runtime-lifetime `QuestAssetSpriteCache`. Screen/view disposal releases UI references but not decoded quest, trader, or map sprites; repeated Tasks opens and raid notifications therefore reuse both completed and in-flight requests. The cache is destroyed only with the QuestMap client runtime.

Raid Tasks is intentionally restricted to In Progress. Mounting ignores a persisted full-map mode, forces the table, and disables the Quest Map tab; the mode dispatcher independently refuses a full-map request while the raid context is active. Notification presentation is a transient stack keyed by quest identity: simultaneous quest changes get separate cards, while another objective update for the same quest replaces that card. Raw progress is capped at `Required` before deciding whether an increase deserves a notification, preventing over-cap mutations from creating false updates.

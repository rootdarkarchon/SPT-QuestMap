# Verified Source Leads and Investigation Targets

These findings are preliminary source leads gathered before implementation. Verify every exact signature against the installed SPT 4.0.13 client.

## Public references

```text
https://github.com/itinybad/SPT-Client-400
https://github.com/DrakiaXYZ/SPT-TaskListFixes
```

The installed client remains authoritative.

## Quest template loading

The public 4.0 client source indicates that main-menu initialization calls:

```csharp
ISession.RequestQuestsTemplates(true)
```

and stores the result in:

```csharp
GClass4014.Instance.GlobalQuestTemplates
```

Profile-specific repeatable templates appear to be stored separately and included by:

```csharp
GClass4014.Instance.GetAllProfileQuestTemplates(profileId)
```

This suggests the client already has enough raw data for the complete quest topology.

Verify:

- exact local method names/signatures;
- timing of template availability;
- repeatable template registration;
- whether seasonal/event filtering requires additional runtime state.

## Live quest state

The public source indicates a live quest collection at:

```csharp
questController.Quests
```

`QuestBookClass` appears to construct live `QuestClass` instances from profile data and templates.

Do not call `LoadAll()` simply to expose future display nodes. Use raw templates plus live quests keyed by ID.

## Per-trader screen

Likely classes:

```text
EFT.UI.QuestsScreen
EFT.UI.QuestsListView
EFT.UI.QuestView
```

The likely flow is:

```text
QuestsScreen.Show(...)
    -> QuestsListView.Show(...)
    -> QuestsListView.OnQuestSelected(...)
    -> QuestView.Show(...)
```

`QuestsListView.Show` appears to filter live quests by `Template.TraderId`.

`QuestsScreen` appears to own a serialized `QuestView` and `QuestsListView`.

Verify the exact local hierarchy and whether a postfix can hide the vanilla list while retaining the native detail pane.

## Global Tasks screen

Likely classes:

```text
EFT.UI.TasksScreen
EFT.UI.TasksPanel
```

`TasksPanel` appears to display active quests such as:

```text
Started
AvailableForFinish
MarkedAsFailed
```

`TasksScreen` also owns behavior unrelated to the task list:

- quest raid item grid;
- persistent quest item grid;
- transfer button and warning;
- Notes UI;
- note CRUD and search.

Do not treat the global Tasks screen as only a quest list.

## Native detail and actions

Likely class:

```text
EFT.UI.QuestView
```

The public source indicates native methods and behavior for:

- accept/restart through the quest controller;
- complete through the quest controller;
- reroll through the session;
- objective, requirement, reward, and penalty rendering;
- linked-quest notifications and success messages.

Likely native operations:

```csharp
AbstractQuestControllerClass.AcceptQuest(...)
AbstractQuestControllerClass.FinishQuest(...)
ISession.QuestChange(...)
```

## Native handover behavior

Likely classes:

```text
EFT.UI.QuestObjectivesView
EFT.UI.QuestObjectiveView
```

The public source indicates that `QuestObjectiveView`:

- finds eligible handover items;
- distinguishes currencies;
- supports weapon assembly conditions;
- opens the native handover selection window;
- calls `AbstractQuestControllerClass.HandoverItem(...)`.

The first implementations should preserve this behavior rather than duplicate it.

## SPT client mod pattern

`DrakiaXYZ/SPT-TaskListFixes` is a useful pattern for:

- BepInEx plugin metadata;
- `netstandard2.1` client project setup;
- references to `Assembly-CSharp`, BepInEx, Harmony, SPT Reflection, Unity, and TextMeshPro;
- exact-version validation;
- narrow `ModulePatch` implementations.

Do not copy its patch targets without verifying the 4.0.13 client.

## Existing QuestMap code worth reusing semantically

Repository areas:

```text
src/SPTQuestMap/Models/QuestMapDtos.cs
src/SPTQuestMap/Services/QuestGraphRules.cs
src/SPTQuestMap/Services/QuestProfileRules.cs
src/SPTQuestMap/Services/QuestTemplateMapper.cs
src/SPTQuestMap/Services/QuestProfileStateBuilder.cs
src/SPTQuestMap/Presentation/Assets/QuestMapRenderer.mjs
```

Useful semantics include:

- topology/profile separation;
- edge classification;
- prerequisite closure;
- direct-successor highlighting;
- known-plus-frontier visibility;
- faction/event filtering;
- deterministic layered layout;
- separate viewport and profile overlays;
- route markers;
- viewport persistence scoped to topology version.

Do not translate the JavaScript renderer line by line. Port its responsibilities into a Unity-appropriate design.

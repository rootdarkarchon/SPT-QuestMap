# Milestone 0 — Installed-Client Investigation

## Objective

Establish exact source-backed behavior for the installed SPT 4.0.13 client before adding replacement UI.

Do not implement graph UI in this milestone except for tiny diagnostic probes that are necessary to inspect runtime objects.

## Inputs to read

- repository `AGENTS.md`;
- repository `README.md`;
- repository `docs/development-status.md`;
- `SHARED-PROJECT-CONTRACT.md`;
- `VERIFIED-SOURCE-LEADS.md`;
- existing repository architecture/state/action documents when relevant.

## Required investigation

### 1. Validate the exact environment

Record:

- EFT executable version;
- SPT version;
- BepInEx version;
- SPT Reflection version;
- Unity version;
- `Assembly-CSharp.dll` path;
- a stable identifying hash or version for relevant assemblies;
- client plugin directory layout.

Confirm this is the intended SPT 4.0.13 client.

If the installed client does not match, stop and report the mismatch.

### 2. Compare installed signatures with public source

Verify or correct the likely targets:

```text
EFT.UI.QuestsScreen
EFT.UI.QuestsListView
EFT.UI.QuestView
EFT.UI.TasksScreen
EFT.UI.TasksPanel
EFT.UI.QuestObjectivesView
EFT.UI.QuestObjectiveView
AbstractQuestControllerClass
QuestBookClass
GClass4014
LocalQuestControllerClass
MainMenuControllerClass
```

Record:

- exact namespaces;
- exact method signatures;
- relevant private fields;
- relevant events;
- screen lifecycle methods;
- controller action methods.

### 3. Verify quest-template availability

Determine exactly:

- when global quest templates become available;
- whether all normal quest templates are present in the client;
- how profile-specific repeatable templates are registered;
- what `GetAllProfileQuestTemplates(profileId)` returns locally;
- which quests appear in the live quest book;
- whether locked future templates have enough data for read-only display;
- how faction applicability is encoded;
- how event and seasonal applicability are encoded;
- how trader availability is represented;
- which events fire when daily quests reroll or expire.

Do not mutate live quest collections during this investigation.

### 4. Inspect the trader Tasks screen hierarchy

Capture the live hierarchy or reliable serialized-field access for:

- `QuestsScreen` root;
- `QuestsListView` root;
- `QuestView` root;
- left/right split layout;
- scroll masks;
- canvas/sorting configuration;
- input blockers;
- close/back navigation;
- a safe mount point for a graph root.

Decide whether a vanilla-Show-then-postfix strategy is viable.

### 5. Inspect the global Tasks hierarchy

Identify:

- `TasksScreen` root;
- `TasksPanel` root;
- Quest Items roots and grids;
- Notes roots;
- existing toggles/tabs;
- search fields;
- transfer controls and warnings;
- close behavior;
- a safe future layout for four views.

### 6. Trace native action paths

Verify the exact client flow for:

- accept;
- restart;
- complete;
- repeatable reroll;
- ordinary handover;
- partial-stack handover;
- currency handover;
- weapon assembly handover;
- linked quest availability;
- success/reward messages.

Record which events or observable state changes occur after each operation.

## Required documentation

Create or update:

```text
docs/in-game-client-design.md
```

Include:

- environment identity;
- exact patch candidates;
- hierarchy findings;
- data-source findings;
- native action flow;
- event candidates;
- unresolved uncertainties;
- recommended Milestone 1 project references.

Update `docs/development-status.md` using the milestone status template.

## Acceptance gate

Milestone 0 is complete when:

- the exact installed target is verified;
- trader and global Tasks patch points are known;
- quest templates and live-state sources are known;
- native action methods are known;
- no critical next-step design depends only on guessed obfuscated names.

When complete, proceed to `M01-client-project-compatibility-harness.md`.

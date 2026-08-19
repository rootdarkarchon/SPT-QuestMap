# Milestone 8 — 2.0 Stabilization and Release Validation

## Objective

Stabilize and validate the feature-complete 2.0 server/client pair, then prepare it for reliable SPT 4.0.13 distribution.

## Prerequisite

Milestone 7's original implementation checkpoint was accepted on 2026-08-09. Substantial planned 2.0 work followed that checkpoint and crossed its integrated detail, action, tracking, transport, and presentation paths, so the final current-build acceptance belongs to this milestone.

Feature scope is frozen at the completed multi-map integration boundary. Milestone 8 validates and hardens the complete visual and functional work from Milestones 6 and 7 plus the subsequent planned 2.0 additions. It owns regression fixes, performance corrections, automated and live validation, compatibility/fallback verification, documentation, packaging, and release evidence.

M08 does not accept discretionary feature expansion. A regression may expose a missing capability or unsafe assumption that cannot be corrected adequately with a narrow fix. In that case, an unforeseen feature addition is allowed only when it is necessary to restore frozen behavior or safely satisfy an already-frozen requirement. Record the discovered regression, why an ordinary fix is insufficient, the smallest added capability, and its validation. Unrelated improvements and newly desired capabilities remain post-2.0 backlog work.

## Status

**Active.** Implementation scope is frozen and stabilization is ongoing. A prior M07 runtime pass remains useful evidence, but it does not substitute for current-build validation after the later action, refresh, performance, tracking, localization, metadata, and multi-map changes.

## Compatibility handling

Inspect or document conflicts with mods patching:

```text
QuestsScreen
QuestsListView
QuestView
TasksScreen
TasksPanel
QuestObjectivesView
QuestObjectiveView
```

Where practical:

- inspect existing Harmony patches;
- patch narrowly;
- log compatibility warnings;
- provide per-feature disable settings;
- restore vanilla UI;
- avoid silently overriding another full screen replacement.

## Packaging

Produce the verified final layout, expected to resemble:

```text
SPT/user/mods/SPT-QuestMap/
BepInEx/plugins/SPTQuestMap.Client.dll
```

Use the exact layout required by the installed SPT 4.0.13 environment.

Update build/package scripts.

Do not include proprietary assemblies or copied game artwork.

## Configuration and safe defaults

Document and choose release defaults for:

- trader graph;
- global Tasks graph;
- custom detail pane;
- diagnostics;
- viewport persistence;
- compatibility fallback.

Defaults should reflect actual test maturity.

## Documentation

Update:

```text
README.md
docs/in-game-client-design.md
docs/deployment.md
docs/testing.md
docs/acceptance.md
docs/development-status.md
```

Include:

- installation;
- exact supported versions;
- feature overview;
- disabling/recovery;
- known incompatibilities;
- build commands;
- profile backup recommendation;
- debug log guidance.

The known-incompatibilities section must explicitly state that QuestMap's global and trader task tables replace, rather than extend, EFT's native task-list rows. Native-list modifications from Quest Tracker and Task List Fixes do not appear inside the custom table. Note the QuestMap-owned tracking, raid notification/overlay, sorting, filtering, pinning, and task-presentation equivalents, and explain that disabling the relevant QuestMap replacement restores the native integration surface.

## Regression matrix

### Server/browser

Verify:

- server starts;
- `/questmap` loads;
- profile selection and refresh work;
- existing server tests pass;
- browser graph behavior remains correct.

### Trader screen

Verify:

- all traders;
- live and future quest selection;
- native actions;
- repeated opening;
- feature-disabled vanilla behavior;
- forced fallback.

### Global Tasks

Verify:

- In Progress;
- Quest Map;
- Quest Items;
- Notes;
- repeated tab switching;
- feature-disabled vanilla behavior;
- forced fallback.

### Rendering

Verify:

- complete graph;
- filters/search;
- focus chain;
- route highlighting;
- pan/zoom persistence;
- multiple resolutions/UI scales;
- stable performance.

### Browser/in-game parity

Verify side by side that meaningful browser QuestMap functionality is present in-game, including:

- search, trader filtering, future depth, and finished/failed visibility;
- prerequisite, successor, focus-chain, Collector-route, and Lightkeeper-route behavior;
- selected-quest graph navigation and information;
- status, blocker, objective, reward, exclusion, and route presentation;
- viewport, selection, and relevant filter persistence.

Verify visual consistency for:

- global and trader graph controls;
- node, edge, route, status, selection, and focus treatments;
- full-pane trader layout and narrow detail pane;
- typography, spacing, scrolling, information density, and non-color cues;
- every supported resolution and UI scale in the release test matrix.

List each intentional browser/in-game difference with its in-game rationale. Treat unexplained parity gaps as release blockers unless the affected feature is explicitly disabled in the chosen release configuration.

### Profile safety

Using disposable profile backups, verify:

- accept;
- restart;
- partial handover;
- currency handover;
- weapon assembly;
- complete;
- linked quest unlock;
- repeatable reroll;
- persistence after server restart;
- no duplicate actions;
- no client/server inventory divergence.

## Release gate

The project is release-ready when:

- server and client build from documented commands;
- exact SPT 4.0.13 compatibility is enforced;
- trader and global screens work;
- native actions remain authoritative;
- Quest Items and Notes remain intact;
- full graph performance is acceptable;
- failure restores vanilla UI;
- browser QuestMap has not regressed;
- the browser/in-game parity audit passes or records an explicitly accepted in-game-specific deviation;
- visual styling is consistent and usable across the supported resolution/UI-scale matrix;
- packaging and documentation are complete.

## Final report

Use `templates/final-report-template.md` and record the final result in repository documentation or the delivery response.

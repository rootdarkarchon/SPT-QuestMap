# Milestone 8 — Packaging and Release Hardening

## Objective

Prepare the server/client pair for reliable SPT 4.0.13 distribution.

## Prerequisite

Milestone 7 must be complete or deliberately deferred with native detail fallback chosen as the release behavior.

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
- packaging and documentation are complete.

## Final report

Use `templates/final-report-template.md` and record the final result in repository documentation or the delivery response.

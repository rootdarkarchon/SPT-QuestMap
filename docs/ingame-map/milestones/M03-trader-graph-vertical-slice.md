# Milestone 3 — Trader Graph Vertical Slice

## Objective

Replace the per-trader quest list with a functional graph while retaining EFT's native quest detail pane for live quests.

This milestone proves the screen lifecycle and selection bridge. Native action verification is completed in Milestone 4.

## Prerequisite

Milestone 2 topology, overlay, filtering, and layout must be available and tested.

## Patch strategy

Prefer narrow patches around the verified local equivalents of:

```text
QuestsScreen.Show(...)
QuestsScreen.Close()
```

Preferred lifecycle:

1. allow vanilla `Show` initialization;
2. capture session, inventory controller, quest controller, trader, `QuestView`, and vanilla list;
3. hide or disable only the vanilla list root;
4. mount a QuestMap graph in its area;
5. retain native detail and close behavior;
6. dispose graph state on close;
7. restore vanilla state on failure.

Do not suppress the entire vanilla `Show` method unless exact source evidence proves it necessary.

## Graph controller

Implement a trader-screen controller that owns:

- selected trader;
- topology snapshot;
- live overlay;
- trader-filtered visible set;
- selected quest ID;
- graph view lifecycle;
- native detail bridge;
- read-only future detail fallback;
- cleanup.

Prevent duplicate mounts for the same screen instance.

## Minimum graph UI

Support:

- quest node rendering;
- quest name;
- trader portrait or fallback;
- basic status styling;
- prerequisite edges;
- selected-node styling;
- click selection;
- drag panning;
- mouse-wheel zoom;
- fit-to-visible;
- safe empty state.

Runtime-created UI is acceptable for this milestone. Do not block on a polished AssetBundle.

## Trader filtering

Use the same conceptual trader filter as the existing QuestMap:

- only that trader's nodes are visible;
- an edge renders only when both endpoints are visible;
- hidden external prerequisites remain represented as blockers in details when possible.

## Native detail bridge

For nodes with a live `QuestClass`:

- cleanly close or update the previous detail binding;
- activate the native detail root;
- call the verified native `QuestView.Show(...)` path;
- mark the quest viewed where vanilla does;
- prevent duplicate `OnStatusChanged` subscriptions.

For template-only future nodes:

- do not create fake live quests;
- show a minimal QuestMap-owned read-only detail view;
- include status, description, requirements, objectives, and related quest IDs where available;
- expose no action controls.

## Safe failure

On initialization or rendering failure:

- log screen/trader context;
- dispose partial state;
- reactivate the vanilla list;
- preserve native close/back behavior;
- avoid hidden blockers.

## Manual verification

Test at minimum:

- several traders;
- live quest selection;
- template-only future quest selection;
- empty or nearly empty trader graph;
- repeated opening/closing;
- switching traders repeatedly;
- feature disabled;
- forced graph initialization failure;
- normal screen close/back behavior.

## Documentation

Record exact patch targets, hierarchy paths, and lifecycle in `docs/in-game-client-design.md`.

Update `docs/development-status.md`.

## Acceptance gate

Milestone 3 is complete when:

- the trader list can be replaced by a graph;
- live nodes drive the native detail pane;
- future nodes have a safe read-only detail fallback;
- repeated use does not duplicate UI or subscriptions;
- feature disable/failure restores vanilla behavior.

When complete, proceed to `M04-native-actions-reactive-updates.md`.

## Implementation status

Status: **Complete**.

The trader projection, runtime graph, batched edges, interaction controls, exact native live-detail bridge, topology-only read-only detail pane, duplicate-mount cleanup, feature-disable path, and forced-failure path are implemented. Static validation passes 16 graph-core tests and 107 merged server/browser tests; the exact client build has zero warnings/errors. The complete manual runtime matrix passes: normal graph lifecycle and selection, cursor-centered zoom and curved-edge review, feature-disabled vanilla behavior, and deliberate initialization-failure restoration.

The first corrected visual pass accepted the vertical-slice lifecycle but identified center-biased zoom and ambiguous shared edge trunks. Cursor-anchored zoom and stable-port Bézier routing are now implemented for an M03 retest. The requested full-pane graph plus narrow selection-driven detail sidebar is recorded as a mandatory Milestone 7 composition because it depends on the custom detail pane; M03 continues to preserve the native detail pane by design.

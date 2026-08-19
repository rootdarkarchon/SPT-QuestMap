# Milestone 2 — Graph Model and Client Data Adapter

Status: Complete (2026-08-05)

## Objective

Build a tested, Unity-independent graph data layer from raw client quest templates plus live profile quest state.

Do not replace game screens yet.

## Prerequisite

Milestone 1 must be complete and the client project must load safely.

## Static topology adapter

Normalize raw quest templates into a client graph topology containing, where source data supports it:

- quest ID;
- localized name and description;
- trader ID/name/image reference;
- faction applicability;
- location;
- event/season classification;
- prerequisite edges;
- accepted predecessor statuses;
- available-after delays;
- direct level requirements;
- trader loyalty and reputation requirements;
- objective definitions and ordering information;
- rewards and penalties;
- branch exclusions;
- direct successors.

Build stable lookups:

```text
template by quest ID
incoming edges by target
outgoing edges by source
trader by trader ID
```

Gracefully retain malformed or missing references and log them.

## Live overlay adapter

Overlay current state from verified live objects:

- exact quest status;
- whether a live `QuestClass` exists;
- quest visibility;
- objective progress where safely available;
- repeatable expiration;
- player level;
- trader availability;
- loyalty level;
- standing;
- handover readiness where source-backed.

Do not mutate templates, quest books, profiles, inventories, or controllers.

## Shared-core timing

Create `SPTQuestMap.Core` now only if the pure boundary is clear.

Otherwise keep pure models and algorithms in the client project temporarily, with tests, and plan extraction in Milestone 5.

Do not move server code that still imports SPT server enums or services merely to satisfy folder structure.

## Pure graph rules

Implement or adapt tested rules for:

- edge requirement classification;
- prerequisite closure;
- direct successor lookup;
- trader filtering;
- faction filtering;
- seasonal/event filtering;
- known/default frontier;
- active-quest filtering;
- finished/failed filtering;
- stable ordering;
- missing predecessor handling;
- cycle-safe traversal.

## Deterministic layout

Implement a pure C# initial layout:

- horizontal rank derived from prerequisite depth;
- stable ordering within ranks;
- fixed node dimensions;
- fixed row/layer gaps;
- deterministic output;
- cycle-safe fallback;
- no Unity object dependency.

Keep layout separate from status overlays.

## Tests

Add tests for:

- normalization;
- success/failure/started/any-outcome edge classification;
- trader filtering;
- active filtering;
- faction/event filtering;
- frontier selection;
- missing predecessor references;
- cycles;
- deterministic layout;
- overlay refresh without topology/layout rebuild.

Unsupported condition types must become explicit unknown entries rather than fabricated results.

## Diagnostics

Add debug-level summaries for:

- raw template count;
- live quest count;
- normalized node/edge count;
- missing live quests;
- unsupported condition types;
- topology build time;
- overlay build time;
- layout time.

## Documentation

Update:

- `docs/in-game-client-design.md` with final client data model and invalidation rules;
- `docs/development-status.md` with build/test results.

## Acceptance gate

Milestone 2 is complete when:

- the client can construct complete topology without mutating game state;
- live state overlays by quest ID;
- trader and active views are produced;
- layout is deterministic and tested;
- ordinary overlay refresh does not require topology/layout rebuild;
- unknown conditions remain honest and visible.

When complete, proceed to `M03-trader-graph-vertical-slice.md`.

Runtime acceptance captured 561 normalized nodes, 736 edges, and a 25-quest live overlay. The live quest book remained unchanged (`25 -> 25`), `LoadAll` was not called, no templates were injected, and repeated refreshes reused topology and layout. The user confirmed both native Tasks screens remained vanilla; no QuestMap runtime error occurred.

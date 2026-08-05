# Milestone 6 — Global Tasks Screen Replacement

## Objective

Replace the global Tasks task-list area with graph-oriented views while preserving Quest Items and Notes behavior.

## Prerequisite

Milestone 5 must provide a reusable production graph component.

## Target views

Implement four views or tabs:

```text
In Progress
Quest Map
Quest Items
Notes
```

The precise visual placement may adapt to the verified EFT hierarchy.

## In Progress view

Show a graph derived from the source-backed active quest set, likely including:

- `Started`;
- `AvailableForFinish`;
- `MarkedAsFailed`;
- `FailRestartable`;
- other relevant active repeatable states justified by exact behavior.

Do not include completed or deeply locked future quests by default.

Support:

- graph selection and details;
- native actions;
- objective progress;
- handover-ready indicators where available;
- useful trader/status filtering;
- stable selection after updates.

Edges render only where both active endpoints are visible.

## Full Quest Map view

Port the useful existing QuestMap behavior:

- profile-known quests plus immediate future frontier by default;
- show all future quests;
- hide finished/failed;
- trader filter;
- search;
- recursive prerequisite highlighting;
- direct successor highlighting;
- focus chain;
- Collector route;
- Lightkeeper route;
- faction filtering;
- event filtering;
- persistent viewport and selection.

Do not invent client state unavailable from the verified data adapter.

## Quest Items view

Preserve native behavior for:

- raid quest item storage;
- persistent quest item storage;
- item selection;
- transfer direction;
- warnings;
- raid restrictions;
- native inventory transaction execution.

Prefer reusing or relocating existing vanilla roots and grids rather than reimplementing inventory UI.

## Notes view

Preserve native behavior for:

- listing notes;
- creation;
- editing;
- deletion;
- search;
- note count;
- busy spinner;
- input blocking during transactions.

Prefer reusing vanilla Notes components.

## Lifecycle requirements

Tab switching must not:

- recreate native grids repeatedly;
- duplicate note subscriptions;
- leave stale selected quest items;
- leak graph views;
- break back navigation;
- leave an invisible panel intercepting input.

## Fallback

If the global replacement fails, restore the complete vanilla `TasksScreen`, not just the old task list.

## Manual acceptance tests

Test:

- all four views;
- repeated tab switching;
- graph selection and native actions;
- quest item transfers in both directions;
- raid restrictions/warnings;
- Notes CRUD and search;
- repeated open/close;
- replacement disabled;
- forced initialization failure;
- relevant resolutions/UI scaling.

## Documentation

Document exact global screen ownership and tab lifecycle.

Update `docs/development-status.md`.

## Acceptance gate

Milestone 6 is complete when:

- In Progress and full Quest Map views work;
- Quest Items and Notes retain their native behavior;
- native quest actions work globally;
- lifecycle and fallback are reliable.

When complete, proceed to `M07-custom-detail-pane.md`.

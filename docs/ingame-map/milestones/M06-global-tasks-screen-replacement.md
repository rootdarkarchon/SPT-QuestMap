# Milestone 6 — Global Tasks Screen Replacement

## Objective

Replace the global Tasks task-list area with a QuestMap-owned full-surface overlay while preserving the real native Quest Items and Notes behavior beneath it.

The current implementation-to-browser comparison and scope decision are recorded in [`../M06-native-web-parity-audit.md`](../M06-native-web-parity-audit.md). That audit is part of this milestone's acceptance record: its M06-owned functional, visual-foundation, lifecycle, and runtime items must be resolved or explicitly accepted as documented platform differences before this milestone closes.

## Prerequisite

Milestone 5 must provide a reusable production graph component.

## Target views

Expose four logical views from QuestMap-owned controls:

```text
In Progress
Quest Map
Quest Items
Notes
```

Do not relabel or repurpose EFT's regular-task, operational-task, Quest Items, or Notes buttons. Hide the unused native regular and operational task lists and their native selectors beneath the QuestMap overlay. QuestMap owns its own In Progress, Quest Map, Quest Items, and Notes controls across the complete available Tasks surface.

The replacement does not need a Native Tasks escape hatch or Return to Map control. Disabling the global replacement in plugin settings restores the complete vanilla screen. Initialization or runtime failure must also restore vanilla automatically.

## In Progress view

Show a graph derived from the source-backed active quest set, likely including:

- `Started`;
- `AvailableForFinish`;
- `MarkedAsFailed`;
- `FailRestartable`;
- other relevant active repeatable states justified by exact behavior.

Do not include completed or deeply locked future quests by default.

Support:

- graph selection and the temporary read-only M06 selection summary;
- objective progress;
- handover-ready indicators where available;
- useful trader/status filtering;
- stable selection after updates.

Edges render only where both active endpoints are visible.

Present this as a dense task-card surface comparable to EFT's quests-in-progress list, not as a sparsely ranked dependency graph. It may reuse the shared card renderer, but dependency rank must not create empty columns for unrelated active tasks.

## Full Quest Map view

Port the useful existing QuestMap behavior:

- profile-known quests plus immediate future frontier by default;
- show all future quests;
- hide finished/failed;
- level-eligible-only filtering;
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

Ordinary selection must reveal the selected quest's complete applicable recursive predecessor chain even when the current future-depth, finished, trader, or search filter would otherwise hide those predecessors. This is distinct from the dedicated focus action, which also compacts the view to the selected chain and direct successors.

Do not invent client state unavailable from the verified data adapter.

## Daily and Weekly band

Profile-generated operational quests must not be inserted into the ordinary dependency ranks where they can be lost among static quests. Match the web composition:

- render a dedicated repeatable band above the ordinary dependency graph;
- split Daily and Weekly quests into clearly labeled groups, with Daily before Weekly;
- retain established trader ordering within each group;
- show status, objective progress, handover-ready state, and live remaining/expired time where available;
- apply the same search, trader, level, and status/finished semantics as the web band;
- keep repeatables out of ordinary focus-chain and static dependency layout calculations unless a source-backed dependency exists.

The full graph and repeatable band share selection and viewport ownership, but the band remains visually and structurally distinct. The in-game client deliberately has no control that hides Daily/Weekly quests: its equivalent of the web `ShowRepeatables` setting is always enabled.

## Filter, display, and applicability parity

For the same active profile state and the same equivalent filter values, the web and in-game implementations must produce the same visible quest IDs and the same contextual additions. This includes:

- default frontier versus Show All Future;
- finished/failed visibility;
- level-eligible-only filtering;
- trader filtering, including the web behavior for cross-trader prerequisites, unmet prerequisite blockers, and the next useful successor tier;
- search;
- ordinary selection context;
- focused-chain membership;
- the interaction and precedence of those filters when combined.

The only intentional filter difference is that the in-game client does not expose the web control that hides the Daily/Weekly band. Treat that input as permanently enabled in-game; Daily/Weekly quests still obey every other applicable web filter.

The in-game graph must also use the same authoritative applicability result as the web graph for:

- USEC-only and BEAR-only quests;
- active seasonal quests;
- inactive seasonal quests;
- quests classified as event season `None`;
- descendants excluded because their only ancestry is through removed `None`/inactive-event quests.

Selection, Focus, search, trader filtering, level filtering, and Show All Future must never reintroduce a quest excluded by the authoritative applicable set.

Prefer one shared pure filtering/projection contract over separate web and client algorithms. Add a parity matrix that feeds equivalent topology/profile/filter inputs to both paths and compares visible ordinary IDs, repeatable-band IDs, selection context, and focused-chain IDs exactly. Confirm representative cases in the side-by-side runtime test.

## Web-parity and visual foundation

Milestone 6 owns functional parity for graph-level interactions that are meaningful in-game. Use the browser QuestMap as the behavior reference for:

- search and trader filtering;
- future-depth and finished/failed visibility controls;
- prerequisite, successor, focus-chain, Collector-route, and Lightkeeper-route interactions;
- selection, viewport, and filter persistence;
- clear status, edge, route, selection, and focus-chain distinctions.

Do not mechanically copy browser-only behavior. Profile selection is owned by the active game session, and live client updates may replace explicit browser refresh flows. Record every deliberate parity difference rather than leaving it implicit.

Establish the shared in-game visual system here, including:

- graph toolbar and control hierarchy;
- node status palette and selection/highlight treatment;
- edge, route, and exclusion styling;
- typography, spacing, and overview readability;
- text or legend support where color alone is insufficient;
- responsive behavior across the supported resolutions and UI scales.

Collector and Lightkeeper membership are card markers matching the browser route strips, not independent destructive filters. Cards use the existing SPT-served quest image, partial location-banner treatment, and trader portrait when available, with text/initial fallbacks. A selected Finished control means finished quests are visible; deselecting it hides the terminal finished/failed set.

This is the visual foundation for Milestone 7. Milestone 8 must not be the first styling pass.

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

QuestMap provides its own Quest Items button. It does not recreate either grid or any transaction behavior: it drives the verified native Quest Items state/root, raises that real UI branch above the QuestMap overlay, and explicitly ensures the branch is active and visible. Selecting the active custom button again closes it without rebuilding the graph. The original native Quest Items selector remains hidden and unmodified beneath the replacement.

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

QuestMap provides its own Notes button with the same semantics. It drives the verified native Notes state/root, raises that real UI branch above the QuestMap overlay, and explicitly ensures it is active and visible. Neither custom side-overlay button is selected when the Tasks screen first opens. The original native Notes selector remains hidden and unmodified beneath the replacement.

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
- native regular/operational task selectors and lists remain hidden and unmodified beneath the overlay;
- repeated tab switching;
- graph selection and the read-only M06 summary;
- plugin-disable restoration of the complete native task/action UI;
- Daily/Weekly band separation, always-visible policy, other-filter behavior, progress, and expiry;
- exact visible-ID parity with the web graph across individual and combined future, finished, level, trader, search, selection, and focus filters;
- faction and event/seasonal/`None` applicability parity with the web graph;
- quest item transfers in both directions;
- raid restrictions/warnings;
- Notes CRUD and search;
- repeated open/close;
- replacement disabled;
- forced initialization failure;
- relevant resolutions/UI scaling;
- side-by-side interaction comparison with the browser QuestMap;
- readable status, route, selection, and focus distinctions at overview and detail zoom levels.

## Documentation

Document exact global screen ownership and tab lifecycle.

Update `docs/development-status.md`.

## Acceptance gate

Milestone 6 is complete when:

- In Progress and full Quest Map views work;
- Quest Items and Notes retain their native behavior;
- the custom full-surface overlay does not repurpose native task/operation/side-view selectors and plugin disable restores vanilla;
- Daily/Weekly quests remain discoverable in their dedicated split band;
- ordinary and Daily/Weekly visible membership matches the accepted web filtering result, with only the documented absence of the Daily/Weekly visibility filter;
- faction and event applicability matches the accepted web result;
- lifecycle and fallback are reliable;
- the listed graph interactions have browser parity or a documented in-game-specific deviation;
- the shared graph visual system is usable at the tested resolutions and UI scales;
- the M06-owned discrepancies in `M06-native-web-parity-audit.md` are closed, while every retained platform difference is recorded explicitly.

When complete, proceed to `M07-custom-detail-pane.md`.

## Current runtime gate — 2026-08-06

The latest parity correction is implemented and statically validated, but M06 is not closed until the deployed build passes runtime review. In addition to the broader matrix above, explicitly confirm:

- the custom surface covers the former native subheader and reaches the right edge;
- repeated Notes and Quest Items clicks follow `closed -> requested -> closed`, direct switching remains mutual, and each real native surface works;
- the two view tabs, right-bound side controls, left search/filters, trader portraits, canvas controls, and swatch legend do not overlap at tested UI scales;
- an empty click while already clear is a no-op;
- Gunsmith Part 2-like cases show prerequisite gating when the profile already meets the inherited level gate, and unavailable-trader quests do not present as available;
- web vocabulary/progress percentage, completion triangles, end-of-line markers, and Collector/Lightkeeper colors match the browser reference.

The In Progress content arrangement remains open by explicit user direction. Do not redesign it until the user's separate suggestions are captured; do not treat this hold as acceptance of the current horizontal trader spread.

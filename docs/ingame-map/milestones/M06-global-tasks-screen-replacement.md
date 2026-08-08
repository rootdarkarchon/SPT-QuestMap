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

Show a vertically scrollable table derived from the source-backed active quest set, including:

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

This view is not a dependency graph. It has no edges, pan, zoom, Fit, Center, horizontal scrolling, or topology-rank positioning. Its fixed columns are Trader, Quest, Location, Status, Progress, and Tasks. Trader and Quest are distinct sort keys but share one quest-card cell in each row. Location uses the existing SPT banner, Status distinguishes active from ready-to-finish/restartable states, Progress uses the authoritative server percentage where available, and Tasks lists every ordered objective with truthful numeric and bar progress where known.

Trader, Quest, Location, Status, and Progress headers cycle inactive -> ascending -> descending -> inactive. Multiple active keys retain click order as sort priority. With no manual keys, the internal stable order is Trader, Location, Quest. Daily and Weekly are independent leading sections regardless of sorting, use the same active sort criteria within their own section, and are visibly separated from ordinary active quests. Sort state and vertical scroll position persist per profile/topology In Progress scope.

Objective density is bounded: the table shows four matching tasks per quest by default and provides an explicit row expander for the remainder. A persisted filter hides completed tasks. Completed objectives use a checkmark and omit redundant full progress bars; `1 / 1` completion omits the numerical duplicate. Ready-to-turn-in quests report 100% overall progress. Quest and location images are width-driven at their original aspect ratio and vertically centered, so expanding a row never stretches its artwork to the row height.

Quest, Location, Status, and Progress share a fixed top band equal to the usable inner height of the minimum row. Expanding Tasks never enlarges or vertically recenters that band. Aspect-correct quest/map images are clipped to it, image shading does not spill into the neutral remainder of the row, and displayed objective current values never exceed their requirements.

The accepted refinement uses an 82-pixel minimum row with a 78-pixel non-task top band. The In Progress canvas is 90% transparent over EFT's background. Expanded non-task remainders and their absent separators remain neutral/translucent, while Tasks retains a full-height opaque reading surface.

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

The active-task replacement is not allowed to depend on the Tasks screen being open for freshness. During a raid, QuestMap permanently observes the raid-local quest controller with exact active-checker value-change events and a 100 ms incremental fallback poll. Broad menu-time subscriptions must be detached, and the fallback must scan a bounded checker batch rather than hashing the complete active quest set on Unity's update thread. Live EFT quest status and objective progress are authoritative during the raid; the server feed remains authoritative for static topology, applicability, trader availability/gates, routes, and metadata. Do not poll the server profile during a raid because that state is not guaranteed to reflect unsynchronized raid progress. The monitor must expose exact changed objective IDs, a reusable change seam, and periodic performance telemetry.

QuestMap also owns in-raid quest-progression notifications as a product feature. Notifications are limited to effectively tracked quests. Manual tracking is profile-scoped in QuestMap's private `BepInEx/config/SPTQuestMap/tracking-state.json` document and is not represented by an F12 entry. F12 exposes live policies for automatically tracking newly accepted quests, implicitly tracking native favorites, and implicitly tracking quests assigned to the current raid map. Current-map tracking does not include `Any`, transit, or marathon quests. Removing manual tracking cannot override an enabled favorite/current-map policy. Reuse compact fading top-right cards grouped by trader/quest; a newer update replaces its quest/task/count/progress content and resets the visible duration. When aggregate and child objectives mutate together, show the most-specific changed leaf task. Paint numerical progress on the bar, omit redundant status text, and expose overall notification opacity in plugin settings. Preserve quest and objective IDs at the policy boundary.

When the In Progress view is first opened in raid, its default filters are trader All plus the independently enabled Any, Transit, and current-map location toggles. Trader and map choices with no corresponding quests under the other active filters are omitted from their respective filter strips.

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
- live in-raid objective/status updates with the Tasks screen both open and closed;
- tracked-quest in-raid progression notifications, including per-quest replacement/timer reset and simultaneous cards for different quests;
- raid-default All/Any/Transit/current-map filters, including Factory night and Ground Zero high-level aliases;
- native profile-scoped quest favorites: pin/unpin from the narrow left column, persistence after closing/reopening Tasks, and a filtered/sorted Pinned section above Daily/Weekly/ordinary quests without duplicates;
- manual tracking by clicking the row Status cell, profile-scoped persistence, normal `(Tracked)` labeling, and italic implicit tracking from favorite/current-map policies;
- live F12 policy changes, auto-tracking on a newly accepted quest transition, strict current-map implicit tracking excluding Any/transit, and raid notifications suppressed for every effectively untracked quest;
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
- in-raid status/progress remains live without consulting a stale server profile, and the permanent monitor cleanly stops at raid end;
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

The first In Progress table redesign is implemented and awaiting runtime refinement. Verify fixed-width columns at supported resolutions, vertical-only scrolling, all three section boundaries, multi-key sort cycling/priority/persistence, objective row sizing, progress truthfulness, and selection retention during reactive updates.

The table also reuses EFT's native favorite-quest service. SPT stores that set outside `characters.pmc.Quests`, under the profile-scoped sptRegistry key `favorite_quests_<profileId>`. The custom pin buttons must round-trip through that native service, and the Pinned section must be first while still obeying the current projection filters and selected multi-key sort.

In Progress location filters retain independent left-click toggles. Right-clicking a location is an exclusive-selection shortcut: it leaves only that location active, including `Any` as an ordinary location category.

Sorting, trader/status/search/location filtering, completed-task visibility, and per-quest expansion must remain incremental after the initial table render. These interactions must retain the table root and image cache; ordinary filtering/sorting reuses cached rows, while expansion is scoped to the selected row's Tasks cell. Use the M06 table/presentation timing markers in the runtime log to compare repeated operations with first-render cost.

The mounted In Progress controller and table are retained when EFT closes the Tasks screen for navigation. Closing suspends the owned root without disposing its rows; reopening the same native screen resumes that root, rebinds current EFT controllers, and applies any changed overlay state. The first mount prewarms rows for every currently active quest, independent of the persisted trader/search/status/location filters, so enabling a previously inactive filter never triggers a large lazy construction pass. Map-filter selection visuals update in place before the incremental projection update. Hidden retained rows continue to receive targeted quest patches, while a genuine topology or raid-mode transition may rebuild only the content view. Runtime acceptance should show `QUESTMAP_M06_SUSPEND` followed by `QUESTMAP_M06_RESUME ... rootReused=True`, with stable `cachedRows`, across ordinary Tasks navigation.

Ordinary in-raid objective and status changes must also remain incremental. They capture and replace only the signaled quest's live state and refresh only that visible row/card; they must not recapture the full quest book, rebuild the topology overlay, recalculate the global projection, or iterate every visible card. Quest-book membership changes are the explicit exception because they can change generated topology and visible membership. Runtime acceptance should show `QUESTMAP_M06_RAID_PATCH` for normal changes and zero broad `overlayRefreshes` in the corresponding `QUESTMAP_M06_RAID_PERF` interval.

While in raid, In Progress is the only enabled Tasks content mode. Persisted Quest Map state must not cause the full graph to render, and the Quest Map tab must remain disabled until the raid ends. Artwork is cached at client-runtime scope rather than Tasks-screen scope so repeated opens reuse decoded sprites and in-flight requests. Simultaneous objective progress across different quests is presented as a stack with one card per trader/quest identity; effective progress is capped at the requirement so over-cap raw increments are ignored.

## M06 scope and closure ledger — 2026-08-08

M06 grew beyond the original four-view replacement. The following additions are now implemented M06 product behavior and must be preserved, but they must not be used to justify further unrelated expansion:

- the vertically scrollable sortable In Progress table, map/status/trader/search filtering, objective expansion, completed-task filtering, repeatable sections, and native favorites/pinning;
- profile-local manual tracking plus the new-quest, favorite, and strict current-map implicit tracking policies;
- the permanent bounded raid-local objective monitor and targeted per-quest UI patches;
- tracked-quest raid progression notifications, including stacked different-quest cards, capped progress, shared artwork caching, and configurable opacity;
- raid-only In Progress mode/default filters; and
- retention of the In Progress controller, controls, prewarmed rows, scroll state, and artwork across ordinary Tasks-screen navigation.

M06 was **accepted complete by the user on 2026-08-08** after extensive live refinement. The items below are retained as final-polish/regression follow-ups rather than milestone blockers. A reproduced functional regression still belongs to its owning layer, but speculative or uncommon coverage does not block M07.

### Deferred final-polish/test follow-ups

1. Add transactional recovery for a failure during a post-mount mode/projection/content rebuild. The existing early forced-initialization failure is not sufficient; after QuestMap owns the surface, a failed rebuild must dispose the owned UI and restore the complete vanilla Tasks screen.
2. Add one explicit cross-surface parity fixture that drives equivalent applicability/profile/filter inputs through the browser and native adapter boundaries and compares ordinary visible IDs, repeatable IDs, selection context, and Focus membership. Both surfaces now call the shared `QuestVisibilityRules`, and existing core tests cover its behaviors indirectly, but adapter-equivalence is not asserted end to end.

### Deferred runtime coverage

- Reproduce the restart case with persisted inactive map filters and confirm first-load toggles remain visually and functionally synchronized before every quest has ever been displayed.
- Confirm `QUESTMAP_M06_SUSPEND`/`QUESTMAP_M06_RESUME` reuse the same table/root/rows across Tasks navigation, with stable scroll/filter/sort/selection state and no duplicate listeners.
- Recheck Notes and Quest Items bounds, backdrops, search/count placement, repeated toggle-off/direct switching, Notes CRUD, and both quest-item transfer directions.
- Compare representative prerequisite, level, loyalty/standing, pending, excluded, and unavailable-trader states—especially Lightkeeper, Ref, and Jaeger—against the web UI using individual and combined filters.
- Exercise pinning, multi-sort persistence, map-filter left/right clicks, tracking state/policies, repeatable sections/timers, objective expansion, and targeted reactive row updates.
- In raid, confirm current-map defaults, hidden-screen live updates, tracking-only notification eligibility, simultaneous different-quest notifications, correct overlapping-objective selection, bounded performance telemetry, and clean monitor teardown at raid end.
- Complete common-resolution/UI-scale, full/all-future graph, and forced-fallback coverage and archive a clean acceptance log.

### Explicitly not required to close M06

The persistent rich quest-detail pane, native action bridges, trader-screen full-pane redesign, detailed blocker/reward/exclusion explanations, and final fine-grained styling/localization remain M07. Browser-only profile selection/comparison/explicit refresh and exact DOM/mouse-hover behavior remain intentional platform differences.

### Final acceptance record

- User acceptance: 2026-08-08.
- Status: Complete.
- The reported retained-screen/filter-state issue was corrected and accepted.
- The browser/native adapter-equivalence fixture and post-mount failure injection remain useful hardening work, not prerequisites for starting M07.
- Further M06-adjacent bugs may be corrected during final polish if they are reproducible.

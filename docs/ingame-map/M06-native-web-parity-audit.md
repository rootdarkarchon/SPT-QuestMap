# Milestone 6 Native/Web Parity Audit

## Purpose and evidence boundary

This audit compares the current Milestone 6 in-game implementation with the accepted server/Blazor QuestMap. It distinguishes behavior that must be corrected before Milestone 6 can close from work deliberately owned by Milestone 7.

The comparison is based on:

- the current in-game implementation in `GlobalTasksScreenController`, `GlobalQuestGraphProjectionBuilder`, `QuestGraphView`, `QuestGraphCardNodeView`, and the shared edge renderer;
- the accepted web implementation in `QuestMapPageState`, `QuestMapFilters`, `InProgressDrawer`, `QuestDetails`, and `QuestMapRenderer.mjs`;
- the M00 verified native `TasksScreen`/`TasksPanel` ownership model;
- the three existing M06 runtime logs and user screenshots.

The latest card/overlay/parity pass has only static verification. Any row marked implemented but unverified remains an M06 runtime gate.

## 2026-08-06 parity correction status

This status supersedes older “current in-game behavior” cells below where the implementation has since changed.

| Reported discrepancy | Correction now implemented | Remaining evidence |
|---|---|---|
| Overlay left native subheader residue and wasted right margin | The owned surface now spans the full width of the resolved Tasks-screen parent and is raised above all dormant native workspace branches. Only an explicitly opened Notes or Quest Items branch is raised above it. | Runtime-check the black residue is gone and the surface reaches the right edge at supported UI scales. |
| Notes/Quest Items alternated or showed inconsistent button state | Custom state no longer derives from EFT root `activeSelf`. A three-state `None`/`Notes`/`QuestItems` owner invokes the real native opener/closer, settles both native toggles and roots explicitly, updates the two custom buttons, and preserves mutual exclusion/reselect-to-close. | Repeat each button at least four times, switch directly between them, then test Notes CRUD/search and quest-item transfers. |
| Flat, crowded toolbar | Quest Map and In Progress are now first-row tabs. Notes and Quest Items are right-bound. Search and the Future/Finished/level controls form a left filter tool group; a persistent All/trader portrait strip uses web trader order. Center, minus, plus, Fit, Focus Chain, and Clear Selection are an overlay inside the canvas. | Runtime-check legibility and overlap at common resolutions/UI scales. The In Progress content layout is deliberately held for the user's separate follow-up. |
| Empty click rebuilt while already clear | Background clear is now a no-op when neither selection nor focus exists. A real selection still rebuilds the projection when necessary to remove selection-only predecessor context while preserving the viewport transform. | Confirm repeated empty clicks produce no render/rebuild log burst. |
| Gates disagreed with web | Client cards now evaluate the web blocker priority from the active overlay: completed/excluded/active states first, then trader unavailable, pending, level, trader requirements, prerequisite, and available. Level eligibility and trader boundary filters consume the same presentation state. Targeted tests reproduce a high-level Gunsmith Part 2 blocked by its prerequisite and an `AvailableForStart` quest from an unavailable Lightkeeper. | Side-by-side runtime-check representative prerequisite, level, loyalty/standing, unavailable-trader, pending, and excluded quests. |
| Raw EFT status labels and no progress percentage | Card labels now use the web vocabulary (`Available`, `In Progress`, `Ready to Finish`, `Completed`, and gated states). In-progress progress uses the same capped per-objective percentage formula as the web UI. | Runtime-check several partially progressed quests and a quest with unknown counters. |
| Route/status/edge colors differed and route legend was a text line | Status rails, requirement edges, Collector, Lightkeeper, terminal, completion, and selection relation colors now use the web palette. The legend is an opaque bottom-left panel with actual color swatches. | Visual runtime acceptance. |
| Missing completion and end-of-line markers | Completed cards now draw the web-style green top-right triangle/check. Applicable ordinary quests without applicable successors draw the gold right-side terminal bar. | Runtime-check a completed terminal and non-terminal quest plus a future terminal quest. |

Static validation for this correction is 31/31 core tests and 107/107 server/browser tests, with a zero-warning client build. Runtime behavior remains the M06 acceptance boundary.

## Summary

The in-game implementation already shares the authoritative topology/frontier data, pooled graph renderer, cursor-centered zoom, recursive prerequisite selection model, direct-successor selection model, viewport persistence, and native Notes/Quest Items roots. It deliberately replaces the browser's left-side In Progress drawer with a top-level in-game view and uses the active game profile instead of a profile selector.

The relabeled native toggles and Native Tasks escape hatch were transitional and have been removed. Milestone 6 now owns a full-surface custom overlay with custom view/side-overlay controls, keeps the unused native task/operation selectors and lists hidden and unmodified underneath, and uses plugin disable as the deliberate route back to the native task UI.

Milestone 6 is not yet complete. The remaining work is not merely final polish: overlay ownership, Daily/Weekly band composition, exact visible-set/filter parity, Focus semantics, active-task information, status/exclusion communication, lifecycle recovery, and native Notes/Quest Items behavior are part of the M06 acceptance contract. Milestone 7 should not absorb those gaps.

## Discrepancy matrix

| Area | Accepted web behavior | Current in-game behavior | Disposition |
|---|---|---|---|
| Authoritative applicability and future frontier | Uses profile `DefaultVisibleQuestIds`/`AllApplicableQuestIds`; faction and event applicability are server-owned. | Uses the same sanitized sets, but no explicit in-game/web set-parity test currently covers faction-only, active/inactive seasonal, `None`, and removed-event descendants. | M06 blocker: add explicit applicability parity coverage and prove selection/Focus/filters never reintroduce excluded IDs. |
| Complete filter result | Web combines future depth, finished, level eligibility, trader context, search, selection context, and focus to produce `VisibleIds`. Trader mode also retains required cross-trader context and useful successors. | `GlobalQuestGraphProjectionBuilder` is a separate, simpler algorithm: it has no level filter, trader is a plain intersection before selected prerequisites are restored, and Focus intersects the already-filtered result. | M06 blocker: equivalent filter inputs must return exactly the same visible ordinary quest IDs and contextual additions as web. Prefer a shared pure contract and add cross-surface parity tests. |
| Global composition | Graph owns the main surface while auxiliary UI is layered around it. | Current code relabels native regular/daily selectors and adds a Native Tasks escape hatch. | Replace in M06 with a QuestMap-owned full-surface overlay. Hide the unmodified native regular/operation selectors and lists beneath it; no native escape hatch is required. |
| Notes/Quest Items controls | Custom QuestMap controls should expose the real native side views without recreating them. | Current code reuses and changes native toggle-group behavior, and Task Items previously failed to appear. | Replace the selectors in M06: custom buttons drive the real native roots/state, raise them above the overlay, ensure visibility, support toggle-off/mutual exclusion, and leave original native selectors hidden/unmodified. |
| Stale native task description/tooltip | Browser has no native `NotesTaskDescriptionShort` residue. | Code now closes two native tooltips and disables `_notesTaskDescription`; the previous build still showed Russian text and a red loader. | M06 runtime blocker. |
| Search ownership | Web search filters the graph. Notes has its own UI context. | The native shared search field always updates the graph listener, including while Notes is the visible overlay, so note search can also mutate the hidden background graph filter. | Fix in M06: QuestMap owns graph search in its overlay; native Notes search remains native and is active only with the real Notes view. |
| Default/full future depth | Default frontier and Show All Future are explicit and persistent. | `FUTURE` uses the same default/all-applicable sets and persists per profile. | Implemented; runtime verify membership and persistence in M06. |
| Finished visibility | Selected means finished quests are visible. | Header state now uses `!HideFinished`, matching the web semantics. | Implemented; runtime verify in M06. |
| Trader filtering | Direct trader selection uses portraits/names, with All explicit and cross-trader prerequisite context retained. | One text button cycles through every trader. It is functional but slow and difficult to discover, especially with many traders. | Fix the selector UX in M06. Exact browser portrait-strip styling is not required, but direct selection and clear All state are. |
| In Progress filtering | Drawer is grouped by trader and is independent of full-map filters. | In Progress has the shared trader cycle and search, but no useful status filter or visible group hierarchy. | Fix in M06: add useful trader/status filtering and clear trader grouping or equivalent organization. |
| In Progress membership | Web uses its normalized active display state; the in-game contract additionally calls out hand-in-ready, marked-failed, and restartable active cases where source behavior justifies them. | The core active set contains `Started`, `AvailableForFinish`, and `MarkedAsFailed`, but omits `FailRestartable` even though the M06 contract identifies it as relevant. | Fix and unit-test the exact M06 active-state set before runtime acceptance. |
| Ordinary selection | Selected quest, all recursive applicable prerequisites, and direct successors receive distinct treatment; unrelated content dims. | Full-map selection now restores filtered recursive prerequisites and the shared selection model identifies direct successors. Unrelated cards do not dim, and filtered direct successors can be absent. | Runtime-verify predecessor restoration in M06. Add clear selected/prerequisite/successor/unrelated treatment in M06. Direct-successor visibility only needs to match the documented web/filter semantics; Focus must always include it. |
| Clear selection | Clicking empty canvas or Clear Selection removes selection and focus. | No clear-selection control and no background-click clear path exist. | Fix in M06. |
| Focus chain | Focus builds the selected quest, all recursive prerequisites, and direct successors as the compact view; unrelated filters do not accidentally erase the chain. | The projection first applies future, finished, trader, and search filters and then intersects with the focus IDs. A successor absent from the frontier or removed by another filter therefore remains missing. The Focus button is also active-looking when no quest is selected. | Functional M06 blocker: construct the complete applicable focus set, define which filters remain meaningful, disable Focus without a selection, and verify clear/unfocus behavior. |
| Hover preview and double-click focus | Hover previews direct relations; double-click focuses. | Mouse hover has no relation preview and double-click has no special behavior. | Non-blocking parity difference. Reconsider during M07 interaction polish if it remains useful with EFT input conventions. |
| Pan, zoom, Fit, and Center | Cursor-centered wheel zoom, drag pan, Fit, selection centering, and persisted viewport. | These behaviors are implemented by the shared M05 renderer. Rebuilds preserve the numeric viewport transform. | Matched foundation; regression-test global projection rebuilds in M06. |
| Full-map status model | Cards distinguish locked, prerequisite-gated, level-gated, trader-gated/unavailable, available, in progress, ready, completed, failed/excluded, pending, and future states. | Cards distinguish exact live status or a single generic `Locked future` state. The topology/profile/trader data is not yet used for card-level gate classification. | Fix in M06 at card/legend level using only proven client facts or an additional sanitized server-derived display classification; do not clone uncertain SPT availability logic. Detailed blocker explanations remain M07. |
| In Progress information | Each entry shows quest/location, objective progress, and route state. | Cards show quest, trader image/initials, and exact status only. Objective progress, handover-ready state, repeatable expiry, and useful state indicators are absent. | Functional M06 blocker: add objective progress and handover-ready indicators; add expiry for active repeatables where known. Detailed objective rows remain M07. |
| Card artwork | Quest art is cover-cropped; a partial location banner fades into it; trader portrait has a fallback. | Asset loading, quest art, a rectangular right-side map layer, trader portrait, and fallbacks are implemented. Unity `Image` currently stretches sprites rather than cover-cropping them, and there is no banner fade. | In M06, prove assets load and remove obvious distortion/legibility failures. Exact fade, crop tuning, typography, and detailed information hierarchy belong to M07. |
| Route display | Collector/Lightkeeper are labeled colored lower strips and appear in the legend/details. | Split colored lower strips exist but have no label or other non-color cue. | Fix in M06 with labels, legend, or both. Rich route explanation remains M07. |
| Event, branch, terminal, and completion markers | Cards expose event/branch badges, terminal/completion markers, and future dimming. | These markers and future-card dimming are absent. | Add the graph-level event/branch/terminal/future distinction needed for readable parity in M06. Detailed exclusion cause belongs to M07. |
| Overview rendering | At small scale, cards switch to simplified high-contrast shapes while preserving status/selection cues. | Full text/image cards are simply scaled down, making Fit/overview views difficult to read. | Fix an overview LOD/readability floor in M06. Fine styling remains M07. |
| Edge routing | Curved directional edges, separate ports, and arrows. | Shared M05 routing provides distributed ports, cubic curves, deterministic backward lanes, arrows, and batched meshes. | Matched foundation; validate on the full global graph in M06. |
| Edge meaning | Success/failure/started/outcome use distinct colors; failure is dashed; selected paths brighten; inactive/future edges change opacity; legend provides non-color meaning. | Requirement colors, selected-path width/opacity, unrelated dimming, and arrows exist. Failure is not dashed; source/future opacity and a legend are absent. | Fix the non-color distinction and legend in M06. State-sensitive opacity is M06 usability work if the full graph remains visually noisy. |
| Exclusion/branch discoverability | Branch-capable cards and profile exclusion causes are visible and navigable. | Template exclusion rules exist in the shared model but are not rendered. The client overlay does not yet expose a profile exclusion cause. | M06 must mark branch/exclusion-capable cards and failed/excluded state. M07 owns cause text and navigation, adding sanitized data only if needed. |
| Selection summary/details | Browser has a persistent narrow rich details pane with description, gates, blockers, objectives, rewards, relations, and exclusion information. | In-game uses a two-line bottom summary that can overlap the graph. | Intentional temporary difference. Replace with the M07 custom detail pane; do not expand the summary into an ad-hoc second details implementation. |
| Native task actions | Browser is read-only; eventual in-game actions must retain EFT's authoritative accept/restart/complete/handover/reroll flows. | Current code exposes a `NATIVE TASKS` escape hatch. | Remove the escape hatch in M06. The global replacement remains read-only through its temporary summary until M07 adds verified custom action bridges; plugin disable restores the complete vanilla action UI. |
| In Progress layout | Browser uses a trader-grouped compact task list with progress meters. | Uses three dense columns ordered by trader/name, but the shared graph card still reads like a large node and lacks group headers/progress. | Fix the active-task composition in M06. M07 owns final card typography and cross-screen styling. |
| Repeatables | Browser uses a separate band split into Daily and Weekly groups, with trader ordering, filtering, status, progress, expiration, and an optional visibility control. | Repeatables participate as ordinary applicable cards, where they are easily lost among static quests. | M06 blocker: implement the same band and other-filter behavior. The sole accepted filter difference is that in-game has no band-visibility control and always displays matching Daily/Weekly quests. |
| Level-eligible filter | Browser defaults to a level-eligible-only toggle. | No separate level filter exists. Level-gated quests remain part of the graph frontier. | M06 blocker: implement the same level filter and include it in combined visible-set parity tests. |
| Profile, language, refresh, and comparison controls | Browser selects profiles/language, refreshes explicitly, and supports A/B comparison. | In-game uses the active session/profile and reactive client events; no profile picker, explicit refresh, language picker, or comparison mode exists. | Intentional platform difference. Do not add these to M06. Graph-level labels should follow an explicit localization boundary; detail localization continues in M07. |
| Reactive updates | Browser refreshes only on request while preserving UI state. | Client updates from verified quest/profile/inventory events and rebuilds only when projection membership changes. | Intentional improvement for the in-game platform; regression-test viewport/selection retention in M06. |
| Persistence | Browser persists filters, selection/focus, and viewport by graph scope. | Mode, future, finished, trader, search, focus, viewport, and selection persist per profile/topology/view. | Largely matched; test reopen, mode independence, topology rebuild, and stale-scope behavior in M06. |
| Initialization and fallback | A failed replacement must leave the complete vanilla screen usable. | Initial mount failure disposes the partial controller, and the forced diagnostic fails before mutation. Failures during later mode/filter/asset-independent graph rebuilds are not wrapped in a screen-level restore path. | M06 blocker: make post-mount rebuild failure transactional and test a failure after ownership has changed, not only an early forced failure. |
| Lifecycle and native cleanup | Repeated switching/close must not duplicate listeners, roots, grids, subscriptions, or input blockers. | Cleanup code removes QuestMap listeners/roots and restores labels, but the newest overlay ownership changes have not completed the full Notes/Items/reopen matrix. | M06 runtime blocker. |
| Resolution/UI scale and performance | Web is responsive and uses overview rendering/culling. | Shared pooling/culling/batched edges exist, but the full global/all-future surface and new artwork have not been exercised across supported resolutions/UI scales. | M06 runtime/performance gate. |

## 2026-08-07 correction status

- Gate and state classification is no longer reconstructed by the native UI. The client feed carries the exact sanitized state produced by the browser's server-side profile builder, including authoritative trader availability, default/applicable sets, progress, repeatable expiry, and exact unmet-prerequisite IDs. Normal web and native views now call the same pure-core visible-set calculation for future depth, finished/level/trader/search, selection, Focus, direct successors, and prerequisite context; comparison remains browser-only.
- Native interaction now avoids reselect rebuilds, supports double-click Focus, and preserves selection when background-click exits Focus. The chrome defaults to In Progress, uses the web All icon, provides search reset, preserves quest-art aspect ratio, and renders Daily/Weekly as separate timed groups without the former gray band. The surface is full-width and reclaims the empty native subheader strip while remaining explicitly bounded below the Character navigation tab bar.
- Static coverage is 32/32 core and 107/107 server/browser. These changes remain runtime-unverified; the In Progress content redesign remains held for the user's separate design direction.

## Work required to close Milestone 6

The following work remains before M06 can close:

1. Runtime-verify the authoritative feed against Lightkeeper, Ref, and Jaeger unlock states and compare representative individual/combined filter results side by side with the web UI.
2. Complete the In Progress content redesign after the user's held design direction is supplied; its membership and server-derived status/progress inputs are already shared.
3. Exercise the full-surface top-edge geometry, timed Daily/Weekly grouping, aspect-preserving art, search reset, double-click Focus, no-op reselect, Focus-exit selection retention, Notes/Items lifecycle, and reopen persistence.
4. Make and test a post-mount rebuild failure fall back transactionally to the complete vanilla Tasks screen, in addition to the existing early initialization failure.
5. Finish common-resolution/UI-scale and all-future performance coverage, then archive a clean complete runtime log.

## Work deliberately deferred to Milestone 7

Milestone 7 owns the information-rich and detailed presentation layer:

- the persistent narrow custom selected-quest pane;
- descriptions, effective requirements, blocker explanations, mutual-exclusion cause text, objectives and progress rows, rewards/penalties, and relationship navigation;
- custom action buttons that bridge to verified native accept/restart/complete/reroll/handover controllers, while retaining native fallback;
- the trader Tasks full-pane graph plus responsive side-pane redesign;
- final card typography, spacing, banner fade/crop tuning, detail scrolling, and consistent detailed styling between trader/global screens;
- hover/double-click refinements if they remain useful after the M06 input model is accepted;
- rich localization of detail-pane content and unsupported-case fallback messaging.

Milestone 7 must not be used to defer broken filtering, missing active-task progress, unclear graph status/edge/route meaning, native overlay regressions, or unsafe M06 lifecycle behavior.

## Intentional platform differences

The following are not parity defects:

- the active game session replaces the browser profile selector;
- reactive client events replace the browser's explicit Refresh button;
- In Progress is a top-level in-game view instead of a persistent left drawer;
- profile comparison remains browser-only;
- the Daily/Weekly band is always visible in-game because the web repeatable-visibility filter is the sole omitted filter;
- exact browser DOM/CSS composition, mouse-hover behavior, and double-click shortcuts do not need mechanical replication.

Every retained difference must be stated in the final M06 acceptance record rather than being described as exact parity.

# Milestone 7 — Custom Quest Detail Pane

## Objective

Add a QuestMap-owned quest detail pane with better graph navigation and item handover information while retaining verified native action controllers.

## Prerequisite

Milestone 6 global and trader graph screens must be stable. M06 was user-accepted complete on 2026-08-08; reproducible M06 regressions may still be corrected during final polish without reopening its feature scope.

## Status

The original M07 implementation checkpoint was user-accepted on 2026-08-09. The reusable pane is implemented on both the global Tasks workspace and the trader-scoped replacement. Trader Tasks/Quest Map composition uses the same table, graph, selection, detail, tracking, asset-cache, and native-action components as the global screen. That checkpoint covered multiple quest accept, fulfillment, handover, turn-in, successor-unlock, and presentation transitions together with workspace bounds, filters, grouping, shared selection/detail synchronization, pinning, notification opacity, and trader objective expansion.

That acceptance is historical evidence for the reviewed build, while final acceptance of the integrated 2.0 implementation was recorded when M8 closed on 2026-08-19. Substantial planned 2.0 work followed the original M7 checkpoint, including changes that crossed M7's detail, action, reconciliation, and presentation boundaries. Those corrections were completed under M8 without reopening M7 as an ongoing feature milestone.

### Release-facing UI polish

- The client uses one configurable semantic palette for selection, prerequisite and successor emphasis; available, active, ready, completed, failed, level-gated, trader-gated and locked states; and Collector, Lightkeeper and terminal-route accents. The defaults preserve the established QuestMap appearance while F12 color entries allow local adjustment.
- F12 section and entry names are human-readable free text. This is an initial-release contract, so obsolete generated names are not migrated.
- Extensive topology, projection, table, raid-monitor and performance telemetry is controlled by the `Diagnostics / Enable debug logging` checkbox. Compatibility state plus warnings and errors remain visible without debug logging.
- Deliberate compatibility/trader/global initialization failure switches and their production code paths have been removed. Real exact-version validation and safe native fallback remain intact.
- Every custom button created through the shared UI factory, including quest-selection surfaces, uses EFT's native hover/click feedback. Hover color is a slightly dimmer form of the button's normal or active color; it is not a second semantic highlight.
- Trader Tasks has a shared, default-on `Unavailable: Hidden` filter. It affects only the Unavailable group and remains consistent while moving between traders in the same profile session.

### Existing handoff assets

- QuestMap details and native action bridges are mandatory parts of either enabled Tasks replacement. There is no independent detail-pane feature toggle; disabling the relevant global or trader replacement restores that complete vanilla screen.
- The transitional M03 `TraderGraphScreenController`/`TraderGraphView` implementation has been removed. `TraderTasksScreenController` replaces the complete `QuestsScreen` workspace and hosts the shared production components while leaving the surrounding trader navigation intact.
- Global selection now owns a reusable QuestMap pane in place of the temporary M06 two-line summary when the feature is enabled.
- Existing topology/overlay models already carry names, trader/location, status/display state, requirements, ordered objective definitions/live progress, repeatable expiry, route membership, and relationship data. Audit missing reward, exclusion-cause, inventory-eligibility, and native-action capability data before expanding transport contracts.

### First implementation slice

1. **Implemented:** render topology/overlay selection in a default-off 640-pixel right pane constrained to the global Tasks viewport.
2. **Implemented:** reuse native `QuestView`, `QuestObjectiveView`, and `QuestRewardList` behavior for live actions, handover, and reward item interaction; keep future topology-only quests read-only.
3. **Runtime-accepted:** selection and mutually exclusive overlay lifecycle are behaving correctly; a fresh-profile pass covered repeated accept, fulfillment, handover, turn-in, successor-unlock, and presentation transitions. Uncommon quest shapes remain opportunistic regression coverage because the action and reward paths are EFT-owned.
4. **Implemented and runtime-accepted for composition:** the trader replacement reuses the component and parameterized global Tasks/Quest Map components. Bounds, filters, grouping, selection/detail synchronization, and pinning have passed user review.

### Trader workspace implementation

- The global `In Progress` label is now `Tasks`. The trader screen defaults to the same Tasks table and offers the same Quest Map renderer as a peer tab.
- Trader chrome is a single row: Tasks, Quest Map, search/clear, contextual task-or-graph filters, and the right-aligned Quest Description toggle. It deliberately has no trader or location strips.
- Trader Tasks projection includes the selected trader's ready-to-finish, available, active, restartable, level-gated, trader-gated, trader-unavailable, pending, locked, and unknown applicable quests. Completed/failed/excluded/expired quests and prerequisite-gated quests are omitted.
- Trader Tasks sections are Available to Finish, Available to Start, In Progress, and Unavailable. With no explicit column sort, each section orders native handover-eligible rows first, then progress descending, then name.
- Trader Tasks rows always display their complete visible objective list. The global Tasks table retains its compact four-objective limit and per-row expand/collapse control, but trader-scoped rows do not render an expander because their task column is intentionally always expanded.
- Trader Quest Map uses the global selection/focus/legend/canvas behavior. Its trader context adds only direct successors of the selected trader's in-progress quests, including cross-trader successors; selecting a node still restores its complete applicable predecessor chain.
- The full `QuestsScreen` content rectangle is covered, hiding the native list/detail/action workspace without covering the surrounding trader selector/navigation. Native transaction controllers remain the only mutation path and remain disabled during raids.
- The custom surface keeps the `QuestsScreen` top/full width but derives its lower edge from the native list/detail workspace so it cannot cover the persistent bottom main menu.
- Trader context suppresses only the currently selected trader's redundant portrait in Tasks rows and the detail banner; cross-trader successors retain their identity. The trader detail header is two-thirds of the global detail header height.
- Level- and trader-gated status cells include the concrete unmet effective requirement on a second line.
- Trader Tasks membership treats prerequisite gating as an independent exclusion: a quest with any additional prioritized gate remains absent when its authoritative prerequisite-blocker set is non-empty.
- Ordinary accept, handover, and turn-in reconciliation refreshes the authoritative server profile projection before recomputing overlays while retaining the existing topology and layout when their version is unchanged. This is required for status prerequisites such as `Started` and newly unlocked successors. Repeatable replacement remains the isolated generated-topology delta path.
- Trader Tasks/Quest Map filter values are profile-session state shared by every trader workspace instance. Search, level eligibility, completed-task visibility, future depth, and finished visibility therefore remain consistent while switching traders without becoming cross-restart persisted settings.

### First runtime findings

- Do not mutate TMP outline material properties on factory-created labels; some global Tasks paths have no source material and will throw. Use the established UI-shadow treatment.
- The pane must remain above the graph after both direct selection and projection rebuild ordering. It is a sibling overlay bounded to the graph viewport rather than graph content.
- Native live task progress must be captured from `AvailableForFinish` only. Start requirements are not task rows and must never contribute to the displayed overall percentage.
- Lightkeeper and BTR Driver are raid-only quest givers. Their details are informational outside the raid interaction and must not expose accept, finish, restart, replace, or handover controls.
- Rebind the reusable pane on every selection, not only on first construction. Mode/projection rebuilds must restore the pane above the canvas without asking a TMP label for synchronous preferred height during the transition.
- A per-quest detail-render exception must degrade to a contained read-only detail error; it must never propagate through the graph rebuild and trigger the complete native fallback.
- All detail progress uses the shared 0-100 percentage scale. Native reward-list height must be measured/grouped from the rendered two-column container, and only the reward cards—not the native surrounding panel chrome—are retained.
- The inactive In Progress sort state follows canonical trader order, location, and authoritative feed order. Alphabetical quest names are only a user-requested explicit Quest-column sort.
- Selection is shared state, not viewport state. Per-tab pan/zoom may persist independently, but tab changes must neither restore stale selected IDs nor synthesize a first-open selection. Re-clicking a selected In Progress quest clears it and closes details.
- Scroll viewports are transparent interaction/mask surfaces over the pane gradient. Short text must not scroll; long text starts at the top and remains clamped. Task rows share a base height and grow only for real wrapping or numeric progress controls.
- Native reward scrolling is governed by rendered child bounds and two-column row count. Suppress all surrounding native list/container graphics while retaining the instantiated reward-card graphics and interaction components.
- Description and Tasks use bounded content-driven heights rather than fixed allocations; Rewards owns the remainder of the pane. The native reward grid is re-anchored below the custom heading so stock title spacing cannot create an empty band.
- The overlay uses a soft non-interactive left-edge shadow to distinguish it from the graph/table beneath without consuming canvas input or changing the pane bounds.
- Quest-banner state actions and repeatable badges reserve the right edge occupied by Collector/Lightkeeper route strips; neither marker may be covered by accept, turn-in, or replacement controls.
- Native quest transactions may cause deferred `QuestsScreen` observers to reactivate its stock list/detail workspace. The trader replacement reasserts ownership immediately after a mutation and for four bounded late-layout frames; this is not a permanent update-loop guard and does not alter the native transaction itself.

## Rollout strategy

The custom detail pane and native action bridge are integral to both replacement workspaces. The global M06 replacement has no Native Tasks escape hatch and M07 does not reintroduce the old relabeled-native-toggle composition. Disabling the global or trader replacement restores that complete vanilla Tasks UI; initialization failure retains the same full-screen fallback.

## Trader screen composition and styling

Replace the transitional narrow trader graph composition with the intended QuestMap layout:

- let the graph use the full practical width and height of the trader Tasks pane;
- show selected-quest details in a narrow, selection-driven side pane;
- keep the graph usable while details are open;
- preserve the native accept, complete, restart, reroll, and handover controls without covering or displacing them incorrectly;
- adapt the side-pane width or presentation for supported resolutions and UI scales without returning to the narrow graph viewport.

Use the browser QuestMap as the information-density and interaction reference while adapting its composition to EFT's existing navigation and action controls.

Milestone 7 owns the detailed visual pass for:

- quest card information hierarchy;
- detail-pane typography, spacing, grouping, and scrolling;
- blocker, objective, reward, penalty, exclusion, and route presentation;
- status icons, labels, legends, and non-color cues;
- consistent styling between trader and global graph screens.

## Detail contents

### Header

Show:

- quest name;
- trader;
- location;
- exact/display status;
- event classification;
- level and trader requirements;
- route markers;
- repeatable expiration.

### Blockers

Show source-backed blockers:

- prerequisite quests and required statuses;
- player level;
- trader availability;
- loyalty level;
- standing;
- available-after delay;
- mutual exclusion;
- faction/event exclusion;
- unknown/unsupported blockers.

### Objectives

For each visible objective, show where available:

- objective text;
- child/parent structure;
- completion state;
- current/required progress;
- timer;
- found-in-raid requirement;
- eligible owned quantity;
- already handed-in quantity;
- remaining quantity;
- handover-ready state;
- unknown progress indicator.

Preserve source-backed ordering.

### Rewards and penalties

Show:

- money;
- items;
- experience;
- trader standing;
- loyalty effects;
- skill rewards;
- unlocks;
- penalties;
- hidden/unsupported reward indicators.

### Graph navigation — deferred

The following detail-pane shortcuts are explicitly not required for M07 closure and may be revisited only if they prove useful:

- center selected;
- focus chain;
- open prerequisite;
- open successor;
- open exclusion cause;
- back to previous quest.

## Native action bridge

Custom controls must invoke verified native operations for:

- accept;
- restart;
- complete;
- reroll;
- handover.

Do not reproduce server-side validation or profile mutation.

## Handover UX

The first custom handover button should open EFT's native handover picker.

Do not add automatic handover until all of the following are proven:

- eligibility matches EFT;
- stack counts are explicit;
- found-in-raid/durability constraints are enforced;
- attachments/children that will be consumed are visible;
- the exact selection is confirmed by the user;
- the native controller still performs the transaction.

## Feature parity tests

The accepted fresh-profile pass covers ordinary available, active, handover, ready-to-finish, completed, repeatable, and successor-unlock transitions. The following uncommon cases remain opportunistic regression coverage rather than closure blockers because QuestMap reuses EFT's native action/reward controllers:

- locked future quest;
- available quest;
- active numerical objective;
- FIR handover;
- partial handover;
- currency handover;
- weapon assembly;
- ready-to-finish;
- completed;
- failed/excluded;
- repeatable;
- timed;
- modded quest.

Any unsupported case must fall back to native details or remain explicitly read-only.

The user accepted practical browser/in-game parity after extensive side-by-side refinement. Minor presentation differences are intentional and do not block closure. The audit covered:

- graph and selected-quest information availability;
- selection and graph-navigation behavior;
- blocker, objective, reward, exclusion, and route presentation;
- the full-pane graph and narrow detail-pane balance;
- common resolutions and supported UI scales.

Record intentional in-game deviations and unresolved visual or functional gaps. Do not classify an undocumented difference as parity.

## Documentation

Record action bridges, unsupported cases, and parity results.

The global-pane implementation now sizes descriptive scroll content from TMP's rendered bounds after layout rather than retaining a line-count approximation. Native reward cards are initialized through EFT's `QuestRewardList`, then their live container is mounted directly in QuestMap's reward viewport; this preserves native item interactions without inheriting EFT's unused internal title offset.

The panel-separation shadow is a dedicated sibling outside the pane's left edge rather than a child constrained by the pane surface. Native reward layout is normalized for four bounded late-layout frames after first initialization so initial selection and subsequent quest changes produce identical card alignment.

The optional F12 summary preference applies only when summary prose exists. Native action completion explicitly invalidates the selected quest overlay, while Started item-handover objectives expose EFT's native objective bridge without incorrectly waiting for the entire quest to reach `AvailableForFinish`.

Handover availability is also native-owned: after binding the hidden exact-build `QuestObjectiveView`, QuestMap mirrors its stock handover button's active/interactable result and does not offer a custom control when EFT found no eligible item. Action completion never redraws from the old overlay. Ordinary actions wait for reactive reconciliation, while repeatable replacement forces a topology/profile reload because both the removed and replacement quest IDs can change.

Quest-level `TURN IN` and objective-level `HAND OVER` are deliberately separate native bridges. `TURN IN` calls EFT's `QuestView.FinishQuest` (`QuestComplete`), while `HAND OVER` calls the bound `QuestObjectiveView` handover action (`QuestHandover`). Accept, restart, turn-in, and handover schedule one delayed authoritative reconciliation after the native transaction task completes; this avoids racing EFT's quest-book observers without introducing polling. Objective scrolling uses a small layout tolerance and disables its scrollbar when all rows fit.

The selected detail pane is refreshed for both table and full-map overlay updates. Objective allocation and rendering share the same rich-text-aware row-height calculation; markup therefore cannot create phantom wrapped lines or a scrollbar that was absent from the section's initial size calculation.

Quest mutation controls are out-of-raid only. Accept, restart, objective handover, repeatable replacement, and quest turn-in are neither rendered nor bound while an active raid context exists, and every callback rechecks that boundary before calling EFT. The quest banner now carries the established Collector/Lightkeeper route bar at its bottom edge: one route fills the width, while two routes split it equally with labels; the quest title remains above the bar.

Update `docs/development-status.md`.

## Acceptance gate

Milestone 7 is complete when:

- the custom pane provides the required graph-oriented information;
- native action controllers remain authoritative;
- native fallback is available;
- tested quest types do not lose action functionality;
- the trader graph uses the full-pane composition with a responsive narrow detail pane;
- the detailed visual system is consistent across trader and global screens;
- the browser/in-game parity audit has no unexplained functional or presentation gaps.

When complete, proceed to `M08-packaging-release-hardening.md`.

The 2026-08-09 checkpoint satisfied this gate for its then-current build. Because later frozen-scope 2.0 work changed several integrated paths, the final current-build acceptance is intentionally repeated under M08's stabilization and release gate.

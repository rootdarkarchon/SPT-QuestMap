# Milestone 7 — Custom Quest Detail Pane

## Objective

Add a QuestMap-owned quest detail pane with better graph navigation and item handover information while retaining verified native action controllers.

## Prerequisite

Milestone 6 global and trader graph screens must be stable. M06 was user-accepted complete on 2026-08-08; reproducible M06 regressions may still be corrected during final polish without reopening its feature scope.

## Status

Active as of 2026-08-08. Start with the feature-gated detail model/pane and native `QuestView` fallback before changing trader-screen composition or action ownership.

### Existing handoff assets

- `EnableCustomQuestDetails` already exists as a default-off compatibility flag, but patch registration currently rejects it because no M07 UI is installed yet.
- `TraderGraphScreenController` already resolves the exact-build native `_questView`, shows it for live quests, and uses `ReadonlyFutureQuestView` for topology-only future quests. This is the proven fallback/action boundary to preserve.
- Global selection currently owns only the temporary M06 two-line summary. M07 should replace that summary with one shared detail model/pane rather than growing it further.
- Existing topology/overlay models already carry names, trader/location, status/display state, requirements, ordered objective definitions/live progress, repeatable expiry, route membership, and relationship data. Audit missing reward, exclusion-cause, inventory-eligibility, and native-action capability data before expanding transport contracts.

### First implementation slice

1. Build a runtime-neutral selected-quest detail model from the existing topology and overlay without adding action behavior.
2. Render that model in a default-off QuestMap pane for global and trader selections, with explicit unsupported/unknown fields and native `QuestView` fallback.
3. Only after read-only parity is stable, widen the trader graph and introduce the responsive narrow side-pane composition.
4. Bridge native actions one at a time, beginning with opening EFT's native handover picker; do not replace native validation or mutations.

## Rollout strategy

Implement behind:

```text
EnableCustomQuestDetails
```

Retain native `QuestView` fallback until feature parity is manually verified.

The global M06 replacement deliberately has no Native Tasks escape hatch. Until this milestone's custom detail/action bridge is enabled, the global graph remains read-only and disabling the global replacement in plugin settings restores the complete vanilla Tasks UI. M07 must not reintroduce the old relabeled-native-toggle composition.

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

### Graph navigation

Provide:

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

Compare native and custom detail behavior for:

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

Also perform a side-by-side browser/in-game parity audit covering:

- graph and selected-quest information availability;
- selection and graph-navigation behavior;
- blocker, objective, reward, exclusion, and route presentation;
- the full-pane graph and narrow detail-pane balance;
- common resolutions and supported UI scales.

Record intentional in-game deviations and unresolved visual or functional gaps. Do not classify an undocumented difference as parity.

## Documentation

Record action bridges, unsupported cases, and parity results.

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

# Milestone 7 — Custom Quest Detail Pane

## Objective

Add a QuestMap-owned quest detail pane with better graph navigation and item handover information while retaining verified native action controllers.

## Prerequisite

Milestone 6 global and trader graph screens must be stable.

## Rollout strategy

Implement behind:

```text
EnableCustomQuestDetails
```

Retain native `QuestView` fallback until feature parity is manually verified.

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

## Documentation

Record action bridges, unsupported cases, and parity results.

Update `docs/development-status.md`.

## Acceptance gate

Milestone 7 is complete when:

- the custom pane provides the required graph-oriented information;
- native action controllers remain authoritative;
- native fallback is available;
- tested quest types do not lose action functionality.

When complete, proceed to `M08-packaging-release-hardening.md`.

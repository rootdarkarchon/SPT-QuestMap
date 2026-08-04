# Quest and profile state model

This document describes behavior, not exact 4.0.13 type names. Replace provisional names with exact source-backed names during Milestone 1.

## Inputs

Expected authoritative inputs include:

- quest templates;
- English or selected locale strings;
- quest event configuration;
- active seasonal event state;
- USEC/Bear quest configuration;
- trader definitions and image data;
- selected PMC profile;
- server quest-availability helpers where reusable.

## Static quest model

For each quest, normalize:

- ID;
- localized name and description;
- trader ID/name;
- quest image URL/path;
- faction applicability;
- seasonal classification;
- start conditions;
- fail conditions;
- objective conditions;
- prerequisite quest IDs plus required predecessor status;
- direct level requirement;
- direct trader loyalty/standing requirements;
- inherited/effective level and trader requirements;
- mutual exclusion relationships;
- direct successors.

Cache this topology. It should not be rebuilt for every profile refresh.

Prerequisite status arrays are alternatives, not cumulative requirements. Edge presentation therefore classifies an exact success-only condition as success, an exact failure-only condition as failure, `Started` combinations as started-or-later, and a `Success` + failure combination as either terminal outcome. Mixed terminal outcomes must never inherit the dashed red failure-only style merely because `Fail` is one accepted value (for example, First in Line → Shortage accepts both `Success` and `Fail`).

## Effective requirements

Effective level and trader gates are the strongest applicable constraints inherited through prerequisites plus the quest's direct constraints.

Examples:

- level lower bound: retain the highest `>=` / `>` value;
- reputation lower bound: retain the highest lower bound;
- reputation upper bound: retain the lowest upper bound;
- loyalty: retain the highest required LL for each trader.

Do not merge incompatible bounds into misleading single values. Preserve multiple requirements where needed.

## Profile overlay

For each quest, derive a display state from:

1. whether the quest applies to the profile faction;
2. whether its seasonal event is active;
3. whether it was globally excluded as a `None` event chain;
4. explicit profile quest status if present;
5. trader unlock/loyalty/standing state;
6. PMC level;
7. prerequisite status requirements;
8. available-after timers;
9. mutual exclusion outcome;
10. server availability helper result, if exposed.

Prefer the server's own availability decision when accessible. Use local derivation to explain *why*, not to contradict authoritative server state.

## Suggested display-state precedence

A practical precedence is:

1. filtered by faction/event — omitted;
2. completed successfully;
3. permanently excluded by another branch;
4. permanently failed/expired;
5. available to finish;
6. started;
7. available after / pending;
8. available to start;
9. blocked by unavailable trader;
10. blocked by level;
11. blocked by trader LL/reputation;
12. blocked by prerequisite;
13. locked/unknown.

The final implementation may refine this after inspecting 4.0.13 behavior.

## Objective progress

Inspect exact 4.0.13 profile structures for:

- completed condition IDs;
- task-condition counters;
- global counters;
- quest status timers;
- start/finish timestamps;
- available-after timestamp.

For each objective:

- map the objective/condition ID to completed state;
- map relevant counters to current values;
- compare current and required values using the condition's operator;
- expose `current`, `required`, `unit/type`, and `complete` where known;
- mark unsupported mappings as unknown.

Nested counter conditions may represent one user-facing objective. Avoid dumping every low-level child condition as an unrelated objective unless that is how the client presents it.

For an in-progress quest's approximate overall percentage, weight every displayed objective equally. A completed objective contributes `1 / objective count`; an incomplete numerical objective contributes its clamped `current / required` fraction of that same share. Unknown incomplete progress contributes zero rather than inventing progress. For example, one kill out of ten on one of three objectives contributes about `3.3%` overall.

## Objective ordering

Build a stable ordering using, in priority order where applicable:

1. explicit condition `index`;
2. parent/child condition relationships;
3. visibility conditions referencing other condition IDs;
4. dependency edges between objectives;
5. stable source order as the final tie-breaker.

If dependencies form a cycle, preserve source order and log a warning.

## Mutual exclusions

Derive static exclusion edges from quest failure/start conditions that reference other quest IDs and statuses.

Profile-specific exclusion should include:

- the quest that caused the exclusion;
- the status/outcome that caused it;
- whether the excluded quest is permanently impossible or restartable;
- whether the selected profile actually took a branch.

## Collector and Lightkeeper routes

- Collector-route quests are Collector (`5c51aac186f77432ea65c552`) plus every recursive quest prerequisite in the live topology.
- Lightkeeper-route quests are Mechanic's Knock-Knock (`625d7005a4eb80027c4f2e09`) plus every recursive quest prerequisite in the live topology.
- Lightkeeper is considered available only when his ordinary profile trader entry is enabled, Knock-Knock has exact status `Success`, and every recursive prerequisite edge is satisfied by one of that edge's accepted statuses. This preserves 4.0.13's legitimate `Started`/`Fail` alternatives on tutorial and branch edges. Missing route data fails closed.
- Quests in both routes receive split Collector/Lightkeeper markers; quests in only one route receive only that route's marker.

## Future-depth frontier

The 4.0.13 implementation uses this deterministic algorithm:

- `Known` = quests present in the profile plus quests the server currently exposes as available.
- `FutureBoundary` = applicable quests whose derived display state is `Available`, `InProgress`, `ReadyToFinish`, or `Completed`.
- a frontier candidate must be a direct successor of `FutureBoundary` and not already be in `Known`;
- default visible set = `Known ∪ Frontier` with no recursive prerequisite or successor expansion;
- show-all mode = every applicable quest.

Only the direct successor is added at a merge point; its other prerequisite quests are not pulled into view unless they are already known. This keeps Collector visible as the direct successor of a completed prerequisite such as Fertilizers without recursively expanding Collector's remaining prerequisite chains. Unit tests cover one-tier chains, completed predecessors, level-gated chains, and merge successors.

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
- the exact SPT 4.0.13 quest-availability decision and its public comparison/applicability helpers.

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
- each objective's zone trigger IDs, authoritative resolved map IDs, and any unresolved trigger IDs;
- the derived actual-map union for native-`Any` quests, without replacing the native location field.

Cache this topology. It should not be rebuilt for every profile refresh.

An `Any` quest visually uses its actual-map union only when it has at least one zone-bearing objective and every zone on those objectives resolves through the shipped trigger catalog. Objectives without spatial triggers, such as a handover following an in-raid pickup, do not erase resolved map information. If any spatial trigger is unknown, `ActualMapsComplete` is false and presentation retains `Any`; partial task/map assignments remain available for diagnostics and future client filtering.

The native Tasks table treats that data as a second axis rather than rewriting the quest. Location sorting always uses the native location. For a fully resolved Any/Transition quest, enabled concrete map filters determine membership and mapped objective visibility; a map-independent Any objective remains visible with any matching concrete slice. The raid tracked-list projection expands the current raid Mongo ID through server-supplied location aliases, then uses the same task assignments. Factory day/night aliases resolve to `factory4_day`, and Ground Zero low/high aliases resolve to `Sandbox`, so each pair remains one tracking map. Its default-on smart mode then retains only the objective types already classified as useful for in-raid auto-tracking.

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
10. the source-pinned SPT 4.0.13 availability projection.

QuestMap follows the exact visibility/status decision order of `QuestHelper.GetClientQuests` and calls its public faction, event, level, loyalty, and standing helpers. It intentionally does not call `GetClientQuests` itself: that client-payload method mutates shared templates and deep-clones every visible full quest record before filtering rewards, while the sanitized overlay needs only ID/status pairs. Local blocker derivation explains *why* without contradicting the source-pinned result.

### Repeatable overlay

Daily, Scav Daily, and Weekly operational quests are profile-generated rather than members of the cached database topology. QuestMap reads `PmcData.RepeatableQuests` directly and never calls `RepeatableQuestController.GetClientRepeatableQuests`, because that method can expire, generate, and persist quests.

For every saved `Daily` or `Weekly` active-group entry:

- match its generated quest ID against `PmcData.Quests` for accepted/profile status and objective progress;
- use the generated entry's embedded `AvailableForStart` status when no profile status exists, producing the `Available` display state;
- map started, hand-in-ready, success, failure, restartable failure, pending, and expired statuses to the normal display-state vocabulary;
- override the result to `Expired` when the group's `endTime` has passed;
- normalize its generated trader, image, location, localized type/description, objectives, and success rewards into the normal node/detail DTOs;
- include the `Daily_Savage` group as a Daily band while retaining an explicit Scav-repeatable identity marker for every generated node;

These nodes live in the profile overlay and do not change the topology fingerprint, dependency edges, or permanent graph layout cache.

The default-on calendar filter includes these repeatable overlay nodes in the visible set. Turning it off removes only Daily/Weekly nodes and their complete band; ordinary topology quests and their filters are unchanged.

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

The sanitized profile overlay caps a known numeric `current` at its known `required` value before presentation. The raw profile counter is still used for the condition comparator, so capping cannot change completion semantics. This keeps ordinary progress displays bounded and prevents comparison mode from treating `2 / 1` and `1 / 1` as different progress.

For an in-progress quest's approximate overall percentage, weight every displayed objective equally. A completed objective contributes `1 / objective count`; an incomplete numerical objective contributes its clamped `current / required` fraction of that same share. Unknown incomplete progress contributes zero rather than inventing progress. For example, one kill out of ten on one of three objectives contributes about `3.3%` overall.

## Profile comparison

Comparison reuses one cached topology with two independently built sanitized profile overlays. The visible graph is the union of both profiles' normal frontier or all-future sets. A quest applicable to only one faction or event scope remains visible with `Not applicable` on the other side.

A quest differs when applicability, display state, available-after value, exclusion outcome or objective completion/numeric progress differs. Blocker explanations remain profile-specific but do not alone turn an otherwise equal quest state into a progress difference. No universal ahead/behind score is calculated because failure and mutually exclusive branch outcomes are not linearly ordered.

Each differing quest carries an ordered set of categories: profile A only, profile B only, exclusion/branch, status, objective progress and available-after wait time. Categories can overlap. The first category is the card's primary reason and any additional reasons are shown as `+N`; selected details expose every category and exact A/B values.

The default `All quests` comparison keeps the union graph intact and dims matching nodes. `All changes` and the individual category filters remove nonmatching nodes while retaining the selected quest, its recursive prerequisites and its direct successors. Filter counts are calculated after the ordinary frontier, finished, level, trader and search filters but before the comparison category filter. The selected comparison filter persists in browser settings; version-three `differencesOnly` settings migrate to `All changes` or `All quests`.

Hide-finished removes a quest only when every applicable profile state is terminal and hideable. Level filtering removes it only when every applicable profile is level-gated. The default frontier, selection closure, direct-successor behavior, search and trader context otherwise retain their existing rules. Profile-generated repeatables are omitted because their profile-local IDs do not provide a safe equivalence key.

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
- Lightkeeper is considered available only when his ordinary profile trader entry is enabled and Knock-Knock has exact status `Success`. The recursive route remains a display marker; it is not reevaluated as a second trader-unlock condition.
- Quests in both routes receive split Collector/Lightkeeper markers; quests in only one route receive only that route's marker.

## Future-depth frontier

The 4.0.13 implementation uses this deterministic algorithm:

- `Known` = quests present in the profile plus quests the server currently exposes as available.
- `FutureBoundary` = applicable quests whose derived display state is `Available`, `InProgress`, `ReadyToFinish`, or `Completed`.
- a frontier candidate must be a direct successor of `FutureBoundary` and not already be in `Known`;
- default visible set = `Known ∪ Frontier` with no recursive prerequisite or successor expansion;
- show-all mode = every applicable quest.

Only the direct successor is added at a merge point; its other prerequisite quests are not pulled into view unless they are already known. This keeps Collector visible as the direct successor of a completed prerequisite such as Fertilizers without recursively expanding Collector's remaining prerequisite chains. Unit tests cover one-tier chains, completed predecessors, level-gated chains, and merge successors.

When a trader filter is active, QuestMap first applies the configured future-depth, finished, level, search, and trader filters normally. Of the surviving selected-trader quests, only those in `Available`, `InProgress`, `ReadyToFinish`, or `Completed` state form the current boundary. QuestMap adds every applicable direct successor of that boundary, regardless of the successor's trader or gate state. Prerequisite-gated and otherwise locked visible quests do not seed another tier. Added successors still obey the configured finished and level-eligibility filters, and the expansion never recurses.

Selecting a quest while the trader filter is active uses the same selection semantics as the unfiltered graph. The selected quest's recursive prerequisite chain remains visible across trader boundaries, while successor highlighting remains direct-only. Search, finished, and level-eligibility handling remain identical to unfiltered selection.

For every quest already visible under a trader filter, prerequisite blockers from the profile overlay contribute their direct subject quests even when those quests belong to another trader. Because blockers contain only unmet status requirements, satisfied incoming edges at merge quests do not add their predecessors. This blocker-context pass uses a snapshot of the visible set and therefore does not recursively expand an added prerequisite's own blockers.

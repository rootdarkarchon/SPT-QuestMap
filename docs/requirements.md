# Product requirements

## Purpose

SPT-QuestMap is a read-only quest planning and profile inspection page hosted by SPT itself. It combines the static quest dependency graph with live profile state so the user can see what is done, active, available, blocked, excluded, or upcoming.

## Page and identity

- Working name: **SPT-QuestMap**
- Route: `/questmap`
- Target: the user's installed **SPT 4.0.13**
- Host: the existing SPT ASP.NET/Kestrel process

## Profile selector

The page must show all valid server profiles with enough identifying information to choose the correct one, such as nickname, side, level, and profile ID where useful.

Selecting another profile should:

- refresh profile-specific state;
- preserve graph topology and layout where possible;
- restore per-profile UI state when available;
- never mutate the profile.

## Graph content

Every relevant quest node should be able to communicate:

- quest name and trader;
- quest/trader artwork from existing SPT routes;
- effective level gate;
- effective trader loyalty and reputation gates;
- whether the quest's trader is unlocked for the selected profile;
- selected profile faction applicability;
- seasonal applicability;
- current quest status;
- mutual exclusion state;
- objective definitions and progress;
- predecessor and successor relationships;
- status required on each prerequisite edge.

## Default graph scope

The default view should avoid dumping the entire future graph on the user.

Visible by default:

1. all quests represented in the selected profile, including completed, failed, active, available, pending, and expired states;
2. quests the server currently considers available or otherwise unlocked;
3. direct future successors forming the next dependency tier.

Not visible by default:

- deeper locked future descendants beyond that next tier.

A `Show all future quests` toggle reveals the complete applicable graph.

Codex must document the final frontier rule and cover it with tests so behavior is deterministic.

## Hide-finished control

A single control may hide both:

- successfully completed quests;
- quests that can never be completed for this profile because they are permanently failed, expired beyond recovery, or excluded by a mutually exclusive branch.

Restartable failures must remain visible.

## Quest selection and focus

Ordinary node selection:

- highlights the selected node;
- highlights all recursive prerequisites and their edges;
- highlights direct successors only;
- displays the objective/detail panel;
- does not change graph membership.

Trader filtering must not change ordinary selection semantics or truncate the selected quest's recursive prerequisite chain when prerequisites belong to other traders.

Outside selection, a trader-filtered quest with unmet prerequisite blockers must show those direct blocking predecessors across trader boundaries. Satisfied predecessors at a multi-prerequisite merge are not added solely for context, and this incoming expansion is not recursive.

Focused-chain action:

- filters and compacts to the selected quest;
- includes all recursive prerequisites;
- includes direct successors only;
- preserves the current zoom;
- keeps the focused node at approximately the same screen position;
- is cleared on deselection or an explicit clear action.

## Objective panel

Clicking any node must show objective definitions, even for locked future quests.

For active/profile-known quests, additionally show:

- completed objectives;
- current/required counter values where recoverable;
- available-after timing where relevant;
- ready-to-hand-in state;
- unsupported progress as unknown rather than fabricated.

Objective order should follow explicit `index`, `parentId`, visibility/dependency conditions, or equivalent 4.0.13 fields. Interdependent objectives should be presented in dependency order.

## State styling

The exact palette is flexible, but states must remain distinguishable without relying only on text. Include a legend.

At minimum visually distinguish:

- locked future;
- level-gated;
- trader-gated;
- trader unavailable;
- available;
- in progress;
- ready to finish;
- completed;
- failed;
- permanently excluded;
- available-after/pending;
- seasonal/event where active.

## Mutual exclusions

Static template data should identify branches where one quest outcome fails or excludes another quest.

Profile overlay should distinguish:

- possible alternatives not yet chosen;
- chosen/completed branch;
- other branch excluded because of that choice;
- ordinary failure unrelated to branch exclusion.

Do not mark every failed quest as mutually excluded.

## Faction handling

- Read faction from the selected profile.
- Include only quests applicable to that faction.
- Shared quests remain visible.
- No manual USEC/Bear toggle is needed.

## Seasonal handling

Use authoritative 4.0.13 event/quest configuration and event activity.

- Active Christmas/Halloween quest chains may appear.
- Off-season event quests are hidden and excluded from totals.
- Event entries classified as `None` remain excluded completely.
- Descendants reachable only through excluded `None` event quests are excluded as well.
- Do not rely on quest names or descriptions to infer seasonality.

## Traders

Show whether each trader is currently usable/unlocked for the selected profile. Reflect:

- profile trader unlock state;
- current loyalty level;
- current standing/reputation;
- effective inherited quest requirements.

## Refresh

A visible refresh button should reload:

- profile list if needed;
- selected profile state;
- active seasonal event state;
- any server-side availability overlay.

Refreshing must not automatically reset zoom, pan, selection, filters, or focused-chain state unless the selected quest/profile no longer exists.

## Persistence

Use browser storage with a versioned key. Persist at least:

- selected profile ID;
- selected quest ID;
- focused quest ID;
- show-all-future setting;
- hide-finished setting;
- search/trader filters if present;
- pan/zoom;
- optional panel sizes or collapsed state.

Scope viewport/layout state to a topology/version fingerprint so stale coordinates are safely ignored after major graph changes.

# Acceptance checklist

Status reflects the user's browser reviews through 2026-08-04. The installed SPT 4.0.13 release is accepted as usable, performant, and observably current. Three unchecked cases remain deferred because no representative runtime state or asset failure is currently available; they are not release blockers.

## Integration

- [x] Mod builds against the user's matching SPT 4.0.13 environment.
- [x] No installed SPT DLL was decompiled.
- [x] Page is reachable at `/questmap` through the existing SPT server.
- [x] Authentication is not applicable; it was explicitly discarded for this local read-only installation.
- [x] Deployment copies only after a successful build/test pass.
- [x] DLL changes trigger a restart; unchanged DLLs do not trigger an unnecessary restart.

## Profiles

- [x] Every eligible loaded profile appears in the selector.
- [x] Profile entries are distinguishable by nickname/side/level.
- [x] Browser receives sanitized data only.
- [x] Selected profile persists across reloads.
- [x] Deleted/missing selected profiles fail gracefully.

## Quest applicability

- [x] USEC profile does not show Bear-only quests.
- [x] Bear profile does not show USEC-only quests.
- [x] Off-season Christmas/Halloween roots and their dependent quest chains are absent and not counted.
- [ ] Active seasonal quests appear when the server event is active. (Currently off-season; not testable.)
- [x] `None` event quests and their exclusively dependent descendants are absent.
- [x] Trader-unlocked state appears profile-accurate.

## Quest state

- [x] Available, started, ready-to-finish, completed, failed, pending, expired, and locked states render distinctly.
- [x] Level gates are accurate.
- [x] Effective trader LL/reputation gates are accurate.
- [x] Permanently excluded quests identify the branch/outcome that excluded them.
- [ ] Restartable failures are not treated as permanently hidden. (Not practically testable yet; minor risk.)
- [x] Edge colors accurately reflect predecessor success/failure/status requirements.

## Objectives

- [x] Clicking any quest shows objective definitions.
- [x] Active quests show completed conditions.
- [x] Numerical counters show current and required values where supported.
- [x] Unknown/unsupported counters are not fabricated.
- [x] Objective ordering respects explicit indices and dependencies. (Accepted; any residual ordering discrepancy is non-blocking.)
- [x] Ready-to-finish quests are obvious.
- [x] Selected quest details list every `Success` reward from the SPT quest template, including rewards flagged `unknown` or `isHidden`, with localized item/trader/skill names where available.

## Location and localization

- [x] Selected quest details show the localized SPT map name for map-specific quests.
- [x] A quest is labeled Any location only when its template location is actually `any`.
- [x] Specific-map graph cards crossfade their quest banner into SPT's map banner, while the detail location entry shows the centered map banner with vertical fades and uses Norvinsk for Any location.
- [x] The language selector lists the language set installed in the SPT database and persists its selection.
- [x] Changing language updates SPT-owned quest, trader, objective, and map strings without resetting graph selection, focus, pan, or zoom.
- [x] QuestMap-owned display strings are supplied by a server catalog rather than hard-coded into the HTML/JavaScript.
- [x] Shared UI labels/statuses use SPT's selected global locale and custom core controls change for every installed non-English client locale.
- [x] Every installed non-English locale translates all gated states, legend explanations, and detail relationship/blocker headings; `QuestMap` remains invariant. (Automated coverage passes for all 16 locale codes; user confirmed translation behavior.)

## Graph scope and interaction

- [x] Default view shows profile-known quests plus only the next future tier.
- [x] Show-all-future reveals every applicable quest.
- [x] Hide-finished removes completed and permanently failed/excluded quests only.
- [x] Node selection highlights recursive prerequisites and direct successors.
- [x] Focus action compacts to that same subgraph.
- [x] Focus action preserves current zoom and focused-node screen anchor.
- [x] Resize preserves pan/zoom.
- [x] Refresh preserves the current view when IDs/topology remain valid.
- [x] Overview mode retains state and path highlighting.
- [x] Hiding quests preserves each node's canonical dependency column and relative vertical order, removes wholly empty columns, closes vertical gaps within surviving columns, and avoids a disruptive viewport jump.

## Assets

- [x] Quest icons load from existing SPT-served assets.
- [x] Trader portraits load from existing SPT-served assets.
- [x] No copied quest/trader image library is included in the mod.
- [ ] Missing assets have graceful fallbacks. (No missing-asset case available to verify.)

## Performance

- [x] Full applicable graph loads without browser lockup.
- [x] Pan and zoom remain responsive. (Cards remain intact while edges are temporarily hidden; user confirmed performance.)
- [x] Pointer movement does not scan/rewrite all edges.
- [x] Profile refresh does not recompute static topology unnecessarily.
- [x] Filter changes complete without visible multi-second stalls.
- [x] Performance measurements and chosen renderer are documented.

## Implemented corrections — user accepted

1. Gate presentation priority is trader availability, player level, trader LL/reputation, then prerequisite quest gate. Blocker derivation evaluates inherited effective requirements as well as the quest's direct conditions.
2. Selecting a quest reveals its finished prerequisite chain even when Hide finished is enabled.
3. Trader and Hide finished filters are ignored and disabled while focus-chain mode is active.
4. Drag and wheel interaction preserve the complete normal-scale card presentation while temporarily hiding all edges; the full curved edge layer returns when interaction settles.
5. The status stripe and body share one unified outer quest-box geometry. Ordinary card borders are removed; selection, chain, and hover highlights draw a single outer border around the complete card.
6. Recursive prerequisite/direct-successor selection highlights use the unified outer border and are reset/recomputed on every selection.
7. Hover highlights direct predecessor/successor nodes and their connecting edges.

# `ingame-ui` branch changes compared with `main`

## Comparison basis

This document describes the repository state on branch `ingame-ui` at commit `0d1af9e`, compared with `main` at merge base `9b9912f`. It covers the 17 branch commits and the tracked branch contents. Local untracked reference checkouts under `reference/` are not product changes and are excluded.

At this snapshot the branch changes 115 tracked files, adds approximately 23,646 lines, and removes approximately 295 lines. The dominant change is a new native BepInEx/EFT client and a runtime-neutral shared graph library; the existing server-hosted `/questmap` application remains supported and gains several parity and metadata features.

## Player-facing release notes

The main difference from `main` is that QuestMap is no longer browser-only. This branch adds an optional native EFT interface while retaining the existing `/questmap` page. Quest information, graph behavior, progress rules, and visual meanings are shared between the two versions wherever practical.

### QuestMap inside EFT

QuestMap can replace both places where EFT normally shows tasks:

- the global **Character → Tasks** screen; and
- each trader's **Tasks** tab.

Both replacements are enabled by default and can be disabled independently in the F12 configuration. Disabling one restores that complete vanilla screen.

The native interface uses Tarkov's existing quest/trader/map artwork and interface sounds. It does not bundle copied game artwork.

### Global Tasks screen

The global screen gains two main views.

#### In Progress

The In Progress view is a full quest table rather than a simple list. It includes:

- quest, trader, location, current state, progress, route, and repeatable information;
- sortable columns and natural numeric ordering for quest series;
- search plus trader, status, location, and repeatable filters;
- separate sections for pinned, Daily, Scav Daily, Weekly, and ordinary quests;
- native favorite stars and QuestMap tracking controls;
- Scav Daily badges;
- native Accept and Turn In buttons when those actions are available; and
- row hover/selection feedback using configurable QuestMap colors and Tarkov sounds.

Map filters support normal multi-selection. Right-clicking a location makes it the exclusive location filter; right-clicking an already-exclusive location is a no-op.

The table is retained when leaving and reopening Tasks, so cached rows, artwork, filters, selection, and scroll state do not need to be recreated every time.

#### Quest Map

The Quest Map view brings the dependency graph into EFT. It provides:

- all known/current quests plus the next useful future tier by default;
- an option to show every future quest;
- search by quest, trader, or ID;
- trader, finished/failed, level-eligibility, and repeatable filters;
- recursive prerequisite highlighting;
- complete selected downstream-chain visibility and direct-successor emphasis;
- focus-chain mode;
- Collector and Lightkeeper route markers;
- end-of-quest-line markers;
- Daily, Scav Daily, and Weekly repeatable bands;
- status, prerequisite, failure, started, and exclusion edge styling;
- drag panning and cursor-centered wheel zoom;
- plus, minus, Fit, Center Selected, Focus Chain, and Clear Selection controls; and
- persisted viewport, selection, filters, and view state.

In raid, the global Tasks replacement intentionally stays on In Progress. The full graph is not offered during a raid.

#### Notes and Quest Items

EFT's native Notes and Quest Items views remain available next to the two QuestMap views. Their native controllers, transfers, and contents are preserved.

The Quest Items button now shows a warning triangle and count when quest raid items are currently carried on the character. The warning updates directly when inventory state changes.

### Trader Tasks screens

Every trader receives the same two-view workspace:

- **Tasks**, using the shared quest table; and
- **Quest Map**, using the shared graph renderer.

The trader view shows that trader's relevant quest context and the intended one-level future boundary. Selecting a quest can reveal its complete prerequisite/downstream chain across other traders, so cross-trader progression is not hidden.

Search, filters, sorting, the chosen view, graph position, and selected quest are retained per profile and trader. Selection is also retained when leaving the trader page for the Flea Market or another menu and restored when returning, as long as that quest still belongs in the refreshed view.

The trader table omits the global-only favorite column because EFT does not expose the same favorite action there. Unavailable quests are hidden by default but can be shown.

### Quest details

Selecting a quest opens a custom details pane in both global and trader screens.

The quest artwork/name, map/location banner, and state/action strip stay fixed. The content beneath them uses one vertical scrollbar. Description, Summary, Relevant Items, Objectives, and Rewards expand to their required height instead of nesting several small scroll areas.

The pane shows:

- localized quest and trader information;
- location and map artwork;
- exact quest state and progress;
- level, trader availability, loyalty, standing, prerequisite, pending, failure, and exclusion reasons;
- objectives in their defined/dependency-aware order;
- known live objective counts without inventing unavailable progress;
- rewards and penalties using EFT's native reward presentation;
- optional hidden rewards;
- Description and Summary tabs when a summary exists;
- a Relevant Items tab when the quest has relevant items;
- a Scav marker for Scav Daily quests; and
- a Wiki button in the top-right of the quest banner.

Simple Tarkov formatting in descriptions, summaries, objectives, rewards, and item names is preserved through the established safe rich-text paths.

### Quest actions

The custom interface supports the ordinary quest workflow:

- accepting quests;
- restarting eligible failed quests;
- handing over ordinary items, partial stacks, currency, and weapon assemblies;
- turning in completed quests; and
- replacing repeatable quests.

These actions call EFT's initialized native quest controllers and confirmation/reward flows. QuestMap does not implement a separate inventory or profile transaction system.

After an action, the affected rows, cards, details, status, blockers, and newly unlocked quests update without rebuilding the complete graph unless the topology actually changed. Repeatable replacement removes the old generated quest first and then inserts the replacement returned by SPT.

### Live updates

QuestMap reacts to quest status, objective, trader, and relevant inventory changes while the client is running.

- Progress-only changes update the existing interface in place.
- Accepted, completed, failed, or newly available quests update their state and visibility.
- New or replaced repeatables update the generated portion of the graph.
- Existing selection is preserved unless the selected quest genuinely disappears.
- Future/read-only selections become live when the quest materializes.

The client never fills EFT's live quest book with fake future quests.

### Quest tracking

QuestMap adds profile-scoped manual tracking. Clicking the Status cell in the In Progress table toggles manual tracking for that quest.

Tracking can also be enabled automatically for:

- newly accepted quests;
- quests marked with Tarkov's native favorite control; and
- active quests assigned to the current raid map.

Current-map auto-tracking handles the known equivalent map variants. It deliberately excludes Any-location, transit, and marathon quests from that automatic policy.

Manual tracking is stored per profile in QuestMap's BepInEx configuration directory. Native favorites continue to use Tarkov/SPT's existing favorite storage.

### In-raid progress notifications

Tracked quests can show progression notifications during raids. Notifications support:

- quest and trader artwork;
- quest state and objective progress;
- progress bars and status accents;
- stacking and deduplication when several objectives update together;
- a compact text-only mode;
- configurable background opacity;
- configurable display and fade duration; and
- shared artwork caching to avoid repeated loads.

Only effectively tracked quests generate these notifications.

QuestMap keeps a bounded raid-local progress monitor active even while Tasks is closed. It combines targeted quest updates with limited polling for progress that EFT does not consistently publish as an event. The monitor and all overlay state are removed on raid exit.

### In-raid tracked-quest list

Pressing `I` by default opens a compact tracked-quest list grouped by trader. It shows applicable tracked quests, their objectives, and known numeric progress, then fades automatically. The key, display duration, opacity, and fade speed are configurable.

The list includes quests for the current location plus applicable Any/transit context; the stricter exclusion of Any/transit applies only to automatic current-map tracking.

### Daily, Weekly, and Scav Daily quests

Scav Dailies are now supported alongside PMC Daily and Weekly quests in both the browser and native client.

- They retain normal Daily timing/status behavior.
- They are explicitly marked as Scav quests in bands, cards, table rows, and details.
- Available, in-progress, ready-to-finish, completed, and expired repeatables use the normal QuestMap state presentation.
- Generated repeatables remain profile-specific and are not compared between two browser profiles.

QuestMap continues to read the repeatables already saved in the profile. It does not call SPT's mutating repeatable-generation path merely to display them.

### Wiki links

Quest details now include a Wiki button in both the browser and native client.

- The browser opens the quest's validated HTTP(S) Wiki link normally.
- The native client delegates to the default Windows browser without waiting for it to close.
- Wiki links remain available during raids.

The Wiki-link feature is credited as inspired by Tyfon's WikiLinks. WikiLinks source was not referenced or incorporated.

### Relevant quest items

Quests can now list relevant items, especially required keys and Gunsmith equipment.

In the browser:

- the section appears only when relevant items exist;
- a horizontal divider separates it from Description/Summary; and
- flea-ineligible items are labeled.

In EFT:

- Relevant Items appears as a third detail tab only when needed;
- every item shows `(Have: N)`;
- counts include items carried by the player, which is important for keys;
- attachments already mounted on weapons are excluded from the count;
- flea-ineligible items still show their owned count but no Flea button;
- eligible items have a Flea button outside raids; and
- the Flea button uses Tarkov's native **Filter by item** search for the exact template, not Linked Search.

Flea search remains unavailable during raids.

The relevant-item catalog is derived from SPT's distributed quest data and the Escape from Tarkov Wiki. Expanded Task Text is credited only as inspiration for surfacing this information; its dataset is not identified as QuestMap's source.

### Interface settings

The F12 menu adds:

- independent global/trader replacement toggles;
- hidden-reward and preferred-Summary settings;
- automatic new/favorite/current-map tracking policies;
- tracked-list hotkey and duration;
- notification mode, opacity, display duration, and fade duration;
- quest-row hover highlighting;
- mouse-over and click sounds;
- configurable selected, prerequisite, successor, quest-state, gate, Collector, Lightkeeper, and end-of-line colors; and
- opt-in detailed diagnostics.

The feature, tracking, and interface-feedback settings default on. Minimal notifications and detailed diagnostics default off.

### State preservation

The native client preserves relevant state rather than resetting it on ordinary navigation:

- global Tasks view and filters;
- In Progress sorting, filtering, rows, and scroll position;
- graph pan and zoom;
- selected quest and focus state;
- trader-specific view/filter/selection state;
- preferred Description/Summary tab; and
- profile-scoped manual tracking.

Persisted graph state is scoped by topology version so stale coordinates cannot corrupt a changed graph.

### Compatibility and recovery

The native client targets EFT build `0.16.9.40087` with SPT `4.0.13` exactly. It validates the installed client identity and required UI/action targets before activating.

When a replacement is disabled or cannot initialize safely, QuestMap restores the complete vanilla screen. Warnings and errors remain logged even when verbose diagnostics are disabled.

QuestMap's tables replace EFT's native task rows. Mods that patch those rows, especially DrakiaXYZ Quest Tracker and Task List Fixes, cannot inject their row additions into QuestMap's tables. Disabling the relevant QuestMap replacement restores those mods' native integration surface. QuestMap supplies its own tracking, pinning, sorting, filtering, raid notification/list, and task-presentation equivalents.

Objective skipping was investigated but is not included. The branch has no dependency on SPT-Skipper.

### Installation and release layout

The server installation now also includes the shared core and metadata catalog:

```text
SPT/user/mods/SPT-QuestMap/SPTQuestMap.dll
SPT/user/mods/SPT-QuestMap/SPTQuestMap.Core.dll
SPT/user/mods/SPT-QuestMap/Data/metainfo.json
```

The native client installation contains:

```text
BepInEx/plugins/SPTQuestMap/SPTQuestMap.Client.dll
BepInEx/plugins/SPTQuestMap/SPTQuestMap.Core.dll
```

No proprietary EFT/SPT/Unity/BepInEx assemblies or copied game artwork are bundled.

# Contributor appendix

This section summarizes the non-player-facing work supporting the release notes above.

## Architecture

- Added `SPTQuestMap.Client`, a `netstandard2.1` BepInEx client bound to the exact EFT/SPT 4.0.13 ABI.
- Added runtime-neutral `SPTQuestMap.Core`, shared by the server and client for normalized topology, state classification, progress, visibility, selection, deterministic layout, edge routing, spatial indexing, table sorting, and raid-list projection.
- Added read-only `/questmap/client/topology` and `/questmap/client/repeatables` routes. They expose sanitized topology/generated-quest data and no mutation API or raw profile.
- Kept topology, live overlay, layout, projection, selection, and viewport state separate so ordinary progress updates do not rebuild static graph work.
- Added pooled/cullable Unity cards, batched masked edge meshes, transform-only pan/zoom, overview rendering, shared sprite caching, retained Tasks controllers, and bounded diagnostics.
- Preserved EFT's native quest controllers as the only action/transaction path. The client never injects future templates into the live quest book.
- Centralized trader order and objective completion/capping/percentage behavior in the shared core and removed superseded proof implementations and duplicated server rules.

## Browser and server changes

- Added Scav Daily support and explicit Scav identity throughout server DTOs, browser bands/cards/details, client transport, native tables, graph cards, and repeatable deltas.
- Added the startup-cached `Data/metainfo.json` catalog, HTTP(S) Wiki validation, localized item resolution, Flea eligibility, and unresolved-item warnings.
- Added browser Wiki/relevant-item presentation while keeping that descriptive metadata out of the Canvas topology payload.
- Applied shared-core visibility/progress rules to the single-profile browser path.
- Added SPT-compatible first-entry handling and warnings for duplicate profile quest rows and generated/static repeatable ID collisions.
- Updated localization and the architecture, requirements, state-model, performance, testing, deployment, README, and development-status documentation.

## Build, deployment, and packaging

The PowerShell workflow now supports `-Target Client|Server|Both`.

- `scripts/build.ps1` validates requested installed prerequisites, uses isolated artifact roots, runs the relevant test projects, and stages `dist/client` and `dist/server`.
- `scripts/deploy.ps1` synchronizes only the chosen target. Client deployment does not inspect or control EFT. A changed server DLL stops only the exact configured SPT server and relaunches it visibly or uses the supplied restart command.
- Server deployment preserves user-managed summary catalogs.
- Release packaging now requires the server DLL, shared core DLL, and `Data/metainfo.json` under the install-ready `SPT/user/mods/SPT-QuestMap/` path.
- Proprietary assemblies, copied game artwork, and deployed JavaScript/TypeScript files remain excluded.

## Tests and validation

The branch adds `SPTQuestMap.Core.Tests` and expands server/browser regressions for:

- topology normalization, malformed references, status/blocker/edge classification, progress, terminal quests, and exclusions;
- faction/event filtering, `None` descendants, future frontiers, trader boundaries, search, finished/level filters, selection, and focus;
- deterministic layout/routes, spatial culling, table sorting, repeatable deltas, raid tracked-list projection, and server/client rule parity;
- Scav Daily identity, duplicate quest/repeatable collisions, and safe generated-quest handling;
- metadata parsing, URL validation, item resolution, Flea eligibility, rich-text sanitization, and browser details; and
- build, deployment, and package layout invariants.

The latest recorded combined validation reports zero compiler warnings/errors, 46 shared-core tests, and 120 server/browser tests. In-game interaction, layout, and action-flow evidence remains documented as manual validation rather than being inferred from compilation.

## Documentation and comparison inventory

New native-client documentation includes:

- `docs/in-game-client-design.md`;
- `docs/ingame-map/SHARED-PROJECT-CONTRACT.md`;
- `docs/ingame-map/VERIFIED-SOURCE-LEADS.md`;
- `docs/ingame-map/M06-native-web-parity-audit.md`;
- milestone specifications `M00` through `M08`; and
- manual-test, milestone-status, and final-report templates.

At the documented snapshot, the branch differs from `main` across 115 tracked files with approximately 23,646 additions and 295 removals. The detailed chronological implementation and manual evidence remain in `docs/development-status.md`.

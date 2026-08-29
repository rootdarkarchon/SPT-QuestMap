# Development status — current operational handoff

> Last consolidated: 2026-08-19. This is a current-state handoff for Codex, not an append-only development diary.
>
> **Maintenance rule:** update existing sections in place. Keep at most 8 short entries under **Final M8 changes**. Do not accumulate old PIDs, superseded hashes, screenshot-by-screenshot refinements, or full implementation narratives here. Git history and the archived long status are the source for archaeology.

## Status at a glance

- **Release:** `2.0.1` combined server/web + in-game client.
- **Accepted browser baseline:** the server/Blazor Milestone 4 application and difference-first profile comparison originally completed as `1.3.0`, retained in the `2.0.1` product.
- **Milestones:** in-game M0–M8 are complete and user-accepted. The `ingame-ui` work has been merged into `main` and is closed as the completed 2.0 in-game implementation.
- **Scope:** the 2.0 feature scope is complete and remains frozen at the server-authoritative multi-map/task-location integration. Further 2.0 work is maintenance: verified regressions, compatibility corrections, and release servicing only. New feature work belongs in a separately approved post-2.0 scope.
- **Blockers:** none.
- **Next work:** live-verify that FIR item drop/search/pickup cycles do not repeat a prior progress notification and that an Icebreaker-expanded map strip scrolls horizontally without clipping.
- **Current validation:** zero compiler warnings/errors; **94/94 shared-core tests** and **166/166 server/browser tests**.
- **Current combined package:** `artifacts/release/SPT-QuestMap-2.0.1.zip`, 15 entries / 12 files, 835,532 bytes, SHA-256 `43762BCE3A9209B28C2955EE374878C7C5A3995FD3419286081287EDA92BCF7F`. Root inspection found zero escaping files, zero `.js`/`.ts` files, and only the four expected QuestMap project DLL entries. All assemblies report `2.0.1.0`; package DLLs are client `95F0507BC0754AB30C50331504974E232C105BDBD51D2503F329024D955BDDA1`, shared Core `5F1E1C1425954F8CD1F02E70ABE2FC68896025E6BEF1198EC1073001BBD47399`, and server `DC3075E2D65827CD46821B34E587020DE4777B0F105CD784C1EC9F18E8333E95`. This testing artifact has not been deployed to the live install.
- **Current installed state (read-only verification):** the live install remains on `2.0.0.0` and does not match the `2.0.1` testing artifact. Installed SHA-256 values are client `4969B5EAB203989401D64E822C14943428EC89F1D583083A67AD6EAC01D886C5`, client Core `7F7FF4D6F3620BAE261C1AA90A927CEAF9EDBD9DD961797B861B4E17984D94D6`, server `6548FA6AD1E24C0268EE49648F7F5C3AB7E9F5B0C85AA8F89533C7369A44523A`, and server Core `C243719C735304903A2A789DB78BC0FFBC8088A81D2B881EF0C2E427871145C7`.

Every source-changing 2.0 maintenance slice must create a **fresh** combined archive with `scripts/package-release.ps1 -Target Both`; record entry/file count, byte size, SHA-256, and root-containment inspection. Never cite an archive generated before the latest source change.

## Exact runtime target

- **SPT:** `4.0.13`, source commit `2891fd41fd07b6150a2192ac0d24adb93eb72862`.
- **EFT:** `0.16.9.40087`.
- **SPT client:** `4.0.13.0`.
- **BepInEx:** `5.4.23.2`.
- **SPT Reflection:** `4.0.13.0`.
- **Unity:** `2022.3.43f1`.
- **Exact client guard:** private build part `40087` and installed `Assembly-CSharp.dll` SHA-256 `FAEF6F0B9F142F9D047495EC3DCCFD5D6974AC048368DC7045955CF54B117982`.
- All EFT, SPT, BepInEx, Harmony, Unity, Sirenix, TextMeshPro, Newtonsoft, and other vendor references remain external with `Private=false`. Do not copy vendor assemblies into output or release packages.
- The server mod may contain multiple top-level project assemblies, but exactly one deployed top-level DLL may implement `AbstractModMetadata`. `IModWebMetadata` enrolls the mod in SPT web integration. `SptVersion` is exact `4.0.13`.

## Solution architecture

### `SPTQuestMap` — server and browser

- Targets matching installed **net9.0** SPT assemblies.
- Owns the complete post-mod quest topology, localization, profile applicability/state projection, generated repeatables, summaries, Wiki/relevant-item metadata, authoritative objective relevance, task locations, map aliases, and task-map resolution.
- `/questmap` is a mod-owned **Interactive Server Razor** page. CSS and the Canvas module are embedded in the DLL and emitted/imported by Razor.
- The browser receives sanitized snapshots through its Blazor circuit. There is no browser-facing raw-profile API or generic JSON data controller.
- Auth is intentionally absent because SPT 4.0.13 provides no browser login/session acquisition flow. The local install is bound to `127.0.0.1:6969`; changing that bind address exposes `/questmap` to reachable clients.
- SPT recursively rejects deployed `.js` and `.ts` files as legacy mods. Do not deploy them. Embedded `.mjs` source inside the assembly is valid.

### `SPTQuestMap.Core` — shared pure model

- Targets `netstandard2.1` and has no Unity, EFT, BepInEx, SPT server, ASP.NET, Blazor, or JavaScript dependencies.
- Owns normalized graph models, filtering/visibility contracts, state and edge classification, objective completion/capping/percentage rules, selection/focus rules, canonical trader order, deterministic layout, spatial indexing, route calculation, task-location matching, tracking projections, and repeatable-node delta logic.
- Browser/server and native client should share a rule here whenever the behavior is platform-neutral. Do not maintain parallel visibility or completion algorithms without a documented platform reason.

### `SPTQuestMap.Client` — exact-version BepInEx client

- Targets `netstandard2.1` and fails closed outside the exact installed environment.
- The compatibility catalog retains only the four `QuestsScreen`/`TasksScreen` **Show/Close** lifecycle targets. Do not reintroduce the removed `MainMenuControllerClass.ShowScreen` Harmony patch.
- A plugin-owned one-shot coroutine waits for `CurrentScreenSingletonClass` to report `EEftScreenType.MainMenu`, yields one frame, then starts the retry-safe topology warm-up. Repeated main-menu entries cannot duplicate it.
- Owns the global and trader replacement workspaces, shared Tasks table, Quest Map graph, details pane, tracking/pinning presentation, raid monitor, tracked list, notifications, sprite cache, and native action bridges.
- Complete replacement initialization failure restores the corresponding vanilla screen. Disabling a replacement restores the native third-party integration surface.

## Non-negotiable authority boundaries

1. **Never mutate a profile, quest book, inventory, objective counter, or quest template directly.** Accept/restart, turn-in, repeatable replacement, item hand-in, rewards, confirmations, and inventory selection must continue through initialized EFT `QuestView`, `QuestObjectiveView`, `QuestRewardList`, controller, and item-event paths.
2. **Never call `QuestBook.LoadAll()` or inject fake live quests.** Complete future topology comes from QuestMap's read-only server feed, not `/client/quest/list`.
3. **Do not call `QuestHelper.GetClientQuests` for QuestMap projection.** It mutates shared templates and deep-clones complete quest payloads. The lean projection emits only the state needed by QuestMap.
4. **Do not infer objective relevance or map scope on the client.** `InRaidRelevant`, `TaskLocations`, `MapIds`, aliases, and quest-level `ActualMaps` are server-authoritative for static and generated repeatable objectives.
5. **Keep native `Quest.Location` unchanged.** Derived task locations are separate metadata and may intentionally differ from the quest template's native location.
6. **No custom mutations in raid.** Native action controls are not created in raid, and callbacks recheck raid state before invoking EFT.
7. **Preserve topology/layout whenever possible.** Ordinary state changes use small feeds or targeted quest patches; generated-repeatable changes use the repeatable delta path; only real structural incompatibility falls back to a full topology rebuild.
8. **Fail closed, not half-rendered.** Rows and replacement surfaces are constructed transactionally; incomplete replacements stay inactive and the prior complete UI remains usable.
9. **Preserve user-managed data.** Deployment must retain the external `summaries` subtree and shipped `Data` files.

## Server-authoritative quest state

### Availability and applicability

The lean profile projection follows the SPT 4.0.13 decision order:

1. explicit accepted/profile status;
2. faction;
3. active event/season;
4. player level;
5. trader existence/availability;
6. prerequisite status;
7. trader loyalty;
8. trader standing.

Use SPT's public comparison/faction/event helpers for the individual checks. Local graph rules may explain future/locked reasons but must not contradict the authoritative result.

Important accepted cases:

- An explicit profile `Locked` row remains locked; client-payload membership must not promote it to available.
- An unaccepted template containing opaque external `Block` start conditions remains locked. An explicit profile row is authoritative when an external mod unlocks it.
- Unknown custom start-condition types remain represented as opaque/unsupported data; do not invent satisfaction.
- A server-`Available` static quest missing from EFT's native `QuestController` is rejected from the actionable native set and logged once; non-native future/history data remains available for read-only graph context.
- Duplicate profile quest IDs, generated/static collisions, and duplicate objective condition IDs use deterministic **first-entry-wins** behavior with warnings rather than crashing the topology build.
- Jaeger requires **Introduction** success; Ref requires **Easy Money - Part 1 [PVE ZONE]** success; Lightkeeper requires **Knock-Knock** success. Recursive Collector/Lightkeeper ancestry is route presentation, not an additional unlock boolean.
- Seasonal classification propagates through dependent chains. `None` event chains retain their stricter descendant-exclusion behavior when ancestry exists only through excluded nodes.
- Game-edition handling in 4.0.13 affects rewards after visibility cloning, not the sanitized quest ID/status projection.

### Objective state

- Profile truth is `PmcData.Quests`, `CompletedConditions`, and `TaskConditionCounters`; live client checkers take precedence for in-raid changes.
- Objective completion uses the actual comparator and caps displayed current values at the configured requirement. Boolean `1 / 1` objectives do not show redundant numbers/bars.
- Server dependency/source order is authoritative for objective display; native details, the Tasks table, and the browser must not independently re-sort it.
- `oneSessionOnly` and `doNotResetIfCounterCompleted` are transported. A resettable active counter can correctly downgrade stale completion after death.
- Unknown future/modded objective types default to **in-raid relevant** to avoid silently hiding work.
- WTT CommonLib `CounterCreator` + nested `Salvage` / `LeaveItemAtLocation` children are display-compatible. Only the outer counter contributes to overall quest percentage; nested rows remain non-actionable.
- Repeatable text supports Daily/Weekly/Scav Daily, exact exploration exits, specific bot roles such as Killa, `AnyPmc` → `AnyPMC` locale lookup, and localized qualifier ordering.

## Task-location model

Every objective carries an authoritative task scope:

- stable transport/filter ID `no-location`, displayed as **Out of Raid**;
- **Any** as an independent scope, not a wildcard;
- **Transition**;
- one or more concrete maps.

Rules:

- Objective scopes drive global Tasks membership, visible objective rows, omitted-task summaries, map choices, raid matching, smart tracking, and new-quest tracking.
- A mixed Customs/Any/Out-of-Raid quest shows only objectives matching the selected scopes; all three filters are required to see the complete task list.
- Concrete maps win header presentation over Any; Any wins Out of Raid. For native Transition quests, `Transition` is shown first beside every complete derived concrete map set, including one-map cases.
- `ActualMaps` and objective `MapIds` remain the data authority; presentation must not rewrite them to add Transition.
- Factory day/night and Ground Zero low/high use alias groups transported by the server. The current raid Mongo ID is expanded through those groups before matching.
- Every known location alias is normalized to the location's canonical internal ID before objective-level and quest-level deduplication. Native Mongo IDs and derived internal IDs must therefore collapse to one map identity rather than producing duplicate localized banners such as Customs/Customs.
- Ambiguous zone IDs are narrowed by the quest's native location only for that individual zone and only when the native canonical map is one of its candidates. Distinct zone IDs continue contributing their maps; Any/Transition provides no preferred map; unmatched ambiguity remains multi-map. Static topology and generated repeatables use the same server-owned rule.
- Tarkov's non-SPT Arena location is semantically excluded from task-map resolution: internal `develop`, Mongo ID `56db0b3bd2720bb0678b4567`, and `Arena` never become filters or aliases. Mixed data such as Safe Corridor retains its valid Reserve scope; Arena-only metadata is suppressed rather than mislabeled Out of Raid.
- Quest-item spawn-map inference applies only to exact `QuestItem` templates in `spawnpointsForced`; ordinary FIR/item objectives are not treated as map evidence.

## Accepted browser behavior

- Profile-aware dependency graph with deterministic layout, culling/spatial hit testing, pan/zoom/fit/focus, selection chains, trader/search/future/finished/level filters, repeatable bands, Collector/Lightkeeper routes, map artwork, terminal/completion markers, rich details, success rewards, failure penalties, summaries, Wiki links, relevant items, and persistence.
- Difference-first profile comparison with symmetric A/B state/objective/gate differences and category filters.
- Server-owned localization for all installed locales: English plus 16 non-English catalogs. QuestMap branding is invariant.
- Sanitized rich text supports the accepted safe HTML/Tarkov tag set; executable/embedded markup and event attributes are rejected.
- `/questmap` remains intentionally unauthenticated and is safe only within the documented bind/exposure boundary.

## Accepted native Tasks behavior

### Workspaces

- Both global Tasks and trader Tasks are complete QuestMap-owned renderers with two modes: **Tasks** table and **Quest Map** graph.
- Global native Notes and Quest Items remain real EFT branches, toggled by QuestMap-owned controls, mutually exclusive, reselect-to-close, and restored on disposal.
- The reusable details pane is intrinsic to an enabled replacement; there is no separate custom-details feature switch.
- Trader Tasks omits the favorite column because the native favorite action is unavailable there. Global Tasks uses EFT's profile-scoped favorite service and pinned grouping.

### Task table and selection

- Global table columns are Trader/Quest identity, Location, Status, Progress, and Tasks; trader context uses shared rendering with trader-specific grouping and no redundant trader portrait.
- Global sections include Pinned, Daily, Weekly/Scav Daily, and ordinary tasks as applicable. Trader sections are Available to Finish, Available to Start, In Progress, and Unavailable.
- Task rows use cached incremental updates. Sorting, filters, completed-task visibility, and expansion must not tear down the complete view, sprite cache, or static cells.
- Current click contract:
  - first click quest A: select A and open details;
  - click selected A again: retain selection and toggle details closed/open;
  - click B while A is selected: switch selection and open B details directly;
  - click table background: clear selection and details.
- The removed double-click task-list setting and custom click-count handler must not return. Quest Map card double-click focus behavior is separate and retained.
- Map-filter omission is shown as `+ x task(s) on ...` with a tooltip; ordinary four-row collapsing and completed-task hiding are separate counts.

### Location controls

Native location-strip order is:

`Reset` → `Out of Raid` → `Any` → `Transition` → concrete maps alphabetically.

- Reset selects every available scope outside raids and Any/current-map/Transition defaults in raid.
- Raid context identity is the canonical location plus the bound EFT quest controller. Every genuinely new raid restores those defaults and rebuilds the table/filter presentation even when two consecutive raids use the same map; suspend/resume within one raid preserves the user's current filter selection.
- Current default mouse behavior uses left-click isolation/reset and right-click inclusion/exclusion; the F12 inversion setting can swap that behavior. Tooltips are derived from current state.
- Trader choices are recalculated immediately from the quest membership produced by active map filters without rebuilding the complete header.
- The global Tasks map strip is horizontally scrollable and clips to its own viewport, so extra locations from mods such as Icebreaker remain reachable without extending past the right edge.

### Details and actions

- Quest/location banners, Description/Summary/Relevant Items tabs, authoritative objective order/progress, native success-reward and failure-penalty cards, Wiki/Flea actions, route markers, and fixed action strip are accepted.
- Accept/Restart, Turn In, Replace, and objective Hand In continue through native EFT views/controllers. Lightkeeper and BTR Driver remain read-only outside their raid-only interaction context.
- Accept/restart, turn-in, and objective hand-in share a one-second real-time duplicate-press guard across table and details controls. Repeatable replacement is intentionally outside that narrow guard.
- Handover eligibility is resolved progressively through initialized native objective hosts; do not synchronously scan every objective during table construction.
- Opt-in objective skipping remains a narrow exact-EFT checker operation, disabled by default, guarded by no-raid/eligibility confirmation, and does not claim standalone persistence before ordinary quest settlement.

### Localization and presentation

- Server/web strings exist in all 17 catalogs. The native client currently has English fallback plus Russian catalog coverage.
- `FitSingleLine` handles fixed-width localized controls with bounded shrinking and ellipsis; multiline body text continues to wrap. Daily/Weekly/Scav badges remain single-line.
- Menu Tasks text size and in-raid overlay text size are separate Small/Medium/Large settings. Changing the raid preset updates visible overlays immediately.
- QuestMap-created pointer controls use native `SimpleTooltip` with delayed, state-aware text and ownership-safe cleanup.

## Tracking and raid behavior

- Manual tracking is profile-scoped in `BepInEx/config/SPTQuestMap/tracking-state.json`; native favorites remain in SPT's `favorite_quests_<profileId>` registry entries.
- Effective tracking combines manual tracking, optional native favorites, and optional current-map policy. Auto-track-new-quests is objective-aware: Any-location quests track only when they contain real in-raid objective work.
- Smart in-raid filtering uses authoritative `InRaidRelevant` and `TaskLocations`; hand-ins and other out-of-raid work do not clutter tracked overlays.
- The tracked-list hotkey and objective-skip shortcut require configured key/modifiers but permit unrelated held gameplay keys such as `W`.
- Raid monitoring uses exact checker events plus a bounded fallback of at most 16 cached `CurrentValue` reads per 100 ms. Do not restore full-book `QuestClass.Progress.GetHashCode()` polling.
- Ordinary raid objective/status changes patch only the affected quest model and visible row/card. Quest-book add/remove/reset remains the structural fallback.
- Raid exit schedules one mandatory server topology reload through the existing coalesced refresh coordinator. Its generation marker clears only after a successful response, so failure remains retryable. The request may finish without a menu quest controller; overlay reconstruction waits for one, and a Tasks screen opened while the request is in flight receives the completed topology through the normal refresh path rather than racing it.
- F12 **Diagnostics → Force reload server topology** exposes the same retry-safe full reload as an explicit outside-raid action. The custom drawer has no hard ConfigurationManager dependency, disables itself in raid or while a reload is running, refreshes open QuestMap surfaces through the coordinator, and logs an unconditional success/failure result.
- Notifications stack by quest/trader identity, replace an existing card for the same quest, cap progress at the requirement, and select one representative overlapping objective. Minimal and artwork presentations share configured fade/timing behavior.
- Notification progress retains an in-raid high-water mark per objective, so inventory-derived FIR values cannot re-notify after a drop/search/pickup cycle unless they exceed the prior value; genuine resettable one-session counters reset that mark when their counter falls.
- A Tasks screen opened in raid forces Tasks mode; the full Quest Map is disabled in raid. Tracked-list and notification assets share the runtime sprite cache.

## Compatibility already implemented

- WTT CommonLib `v2.0.23`: nested Salvage display objectives and opaque `Block` gates.
- Content Backport Prestiges: `PrestigeLevel == 0..5` requirements and localized `PrestigeGated` state.
- Fika `2.3.9`: native handover/turn-in contexts and post-raid hidden-view lifecycle retained.
- Modded malformed data: duplicate quest/objective IDs, malformed child values, unsupported conditions, and missing locale text degrade deterministically instead of aborting the full topology.
- Custom summary discovery under `SPT/user/mods/SPT-QuestMap/summaries`: top-level `<name>.json` is English; `<name>.<language>.json` is localized; deterministic merge order; later files win per language; malformed files are isolated.
- Native-row mods such as Quest Tracker or Task List Fixes cannot inject behavior into QuestMap's complete custom rows. QuestMap owns equivalent sorting/filtering/tracking/raid presentation. Disabling the relevant replacement restores the native integration surface.

## Performance boundaries

Do not regress these accepted design choices:

- Server graph propagation uses indexes/queues and a topological pass, with bounded fallback for malformed cycles; do not restore graph-depth-dependent fixed-point scans.
- Loose-loot/quest-item map data is materialized once during startup by `QuestMapTopologyPreload`; applicable locations are scanned in parallel into isolated results and merged deterministically, while the item-to-map index remains keyed by `MongoId` through objective resolution. Requests must not enumerate locations, dereference `LooseLoot.Value`, or rebuild that index.
- The native topology route serializes its valid JSON directly; do not run the multi-megabyte payload through redundant response regex replacement.
- Ordinary out-of-raid refreshes use the smaller profile/repeatable feed, reuse static topology/layout, batch changed quest IDs, and rebuild geometry only when membership changes.
- In Progress filters/sorts/expansion are incremental; table rows and artwork are reused.
- `QuestAssetSpriteCache` is runtime-wide and limits local asset transport to eight outstanding requests, processing completed decode/callback work in bounded frame batches.
- Main-menu warm-up is non-blocking and retry-safe. A Tasks screen joins the same in-flight request.

Accepted observed scale:

- One-time startup preload: about **8.4 s** for 4,582 items / 19 locations / 138 quest-item spawn mappings; accepted as-is.
- After preload: local 558-quest topology roughly **60 ms**, profile roughly **44 ms**.
- Reporter setup with 1,245 static quests: server request roughly **440 ms**, full client topology load roughly **1.06 s**, including a 5.39 MB response and about 590 ms deserialization.

Treat order-of-magnitude regressions or visible vanilla-screen exposure during first Tasks open as defects.

## Build, test, package, and deployment

### Commands

```powershell
scripts/build.ps1 -Target Both -Configuration Release -SptRoot D:\Tarkov-SPT
scripts/deploy.ps1 -Target Client   # or Server / Both
scripts/package-release.ps1 -Target Both -Configuration Release -Version 2.0.1
```

- `build.ps1` uses the explicit `Client|Server|Both` contract and SDK `--artifacts-path`; stages process-specific output under `dist/client` and `dist/server`.
- A normal complete pass runs the shared-core suite and server/browser suite. `-SkipTests` is allowed only when explicitly requested and must be recorded honestly.
- The server and client project versions must match before packaging.
- Release contents are confined to:
  - `SPT/user/mods/SPT-QuestMap/`
  - `BepInEx/plugins/SPTQuestMap/`
- Inspect the archive for path escape and unintended files. No vendor DLLs or server `.js`/`.ts` files may appear.

### Deployment behavior

- After requested QuestMap code changes pass validation, deployment is the established default unless the current task explicitly opts out or withholds a required live-process action.
- Client deployment synchronizes only `BepInEx/plugins/SPTQuestMap` and intentionally does **not** inspect or stop Tarkov. A mapped-DLL lock while EFT is open is an expected deployment failure, not permission to kill the process.
- Server deployment preserves the external summaries subtree, compares staged/installed DLL hashes, stops only the exact configured `SPT.Server.exe` when changed, and relaunches it in a normal visible console.
- Never poll or wait for server readiness after restart. The user confirms runtime readiness.
- Compare staged and installed SHA-256 values after copy. Do not restart an unchanged server package.

## Final M8 acceptance record

The user accepted the integrated 2.0 in-game UI and closed M8 on 2026-08-19. The following uncommon or environment-dependent cases remain useful post-release smoke/regression coverage, but they are not unfinished milestone work or known blockers:

1. **Task-location semantics:** live Safe Corridor must retain Reserve while excluding Arena; Chemical - Part 1 must not show duplicate Customs; Work Smarter must not inherit Labyrinth from its ambiguous `exit777` zone. Confirm `Out of Raid` labels/tooltips and stable `no-location` transport behavior.
2. **Mixed scopes:** verify one quest containing concrete map + Any + Out of Raid across global table membership, objective visibility, omission summary, headers, and tracked raid list.
3. **Transition:** verify native/web detail and table headers show Transition first with one or multiple derived concrete maps without altering filter/raid data.
4. **Tasks interactions:** confirm the current single-click selection/detail contract, background clear, action controls, map isolate/include behavior, Reset defaults, trader-strip refresh, and new-raid reset versus same-raid resume preservation.
5. **Localization/layout:** validate Russian fixed-control fitting, Out of Raid translations, long badges, repeatable exploration/elimination wording, and the separate in-raid text-size presets.
6. **Preload/compatibility:** confirm one `QUESTMAP_M02_PRELOAD` after main-menu readiness, `resolved=4/4; installedPatches=4`, no old main-menu Harmony interaction, and no conflict with the reported repair patch.
7. **Native lifecycle/actions:** recheck Fika handover/turn-in, post-raid details reopening, one-second duplicate-press rejection, hidden-host cleanup, the mandatory post-raid topology refresh after repeatables change during a raid, and the manual F12 force-reload action.
8. **Modded content:** opportunistically validate WTT Doom Arcade Salvage/Block and Content Backport Prestige templates on installations that actually contain them.

M8 closure retained the successful combined build/test/package evidence above. Future releases should reuse this matrix proportionally to the affected code instead of reopening the completed milestone.

## Final M8 changes

Keep this list short and replace/collapse older entries instead of extending it indefinitely.

- Raid exit now schedules one retry-safe, coalesced server-topology reload; the F12 Diagnostics section also exposes that full reload manually for stale/missing native data without an EFT restart.
- New-raid identity includes canonical map plus EFT quest controller, so every new raid restores raid filter defaults even on the same map while same-raid suspend/resume preserves the user's filters.
- Task-map aliases are canonicalized before deduplication, preventing Mongo/internal aliases from producing duplicate map banners such as Customs/Customs; quest-item spawn inference retains `MongoId` keys end-to-end and parallelizes independent location materialization before a deterministic merge.
- Quest details now preserve the server's dependency-aware objective order, and both browser/native details transport and render SPT `Fail` rewards as a distinct **Penalties for failure** section below success rewards.
- Accept/restart, turn-in, and objective hand-in share a one-second cross-surface press guard and matching temporary control disablement; repeatable replacement remains outside it.
- Server-authoritative per-objective relevance/task scopes and finalized Any/Transition/mixed-map semantics drive every browser/native filter, task, tracking, and raid surface; the hotkey raid list uses full available screen height and current-map-first/Any-second task bands without splitting mixed-scope quests.
- Main-menu warm-up and server topology construction were hardened against visible first-open delay and large-modded-graph regressions; Russian/fixed-width localization and raid text sizing were also stabilized.
- Failed one-session objectives now reset stale native completion through EFT's initialized checker/controller before they can be skipped again; a narrowly stuck `Started` quest whose necessary conditions are all effectively complete can replay one satisfied condition before the ordinary native Turn In flow. Trader Tasks also retains already-started, hand-in-ready, and restartable quests after an exact-status start prerequisite advances beyond the status that originally unlocked them. Compatibility hardening otherwise retains WTT, prestige/external gates, malformed mod data, Arena exclusion, and **Out of Raid** presentation.

## Milestone summary

- **Web M0–M4 / 1.3 baseline:** exact SPT integration, sanitized topology/profile model, Canvas graph, localization, acceptance fixes, repeatables, rewards, summaries, and difference-first profile comparison. Complete.
- **Client M0:** exact installed-client investigation and hierarchy/action tracing. Complete.
- **Client M1:** exact-version project/compatibility harness and safe-disable behavior. Complete.
- **Client M2:** shared graph model, complete read-only topology feed, live overlay adapter, observation hooks. Complete.
- **Client M3:** trader graph vertical slice proving native detail bridge; later superseded by the shared production workspace. Complete.
- **Client M4:** native action ownership and event-driven reactive updates, including viewport/selection preservation. Complete.
- **Client M5:** shared core and production pooled/batched graph renderer. Complete.
- **Client M6:** global replacement, Tasks table, filters, pinning/tracking, retained screen lifecycle, targeted raid monitor, notifications, and tracked list. Complete and accepted.
- **Client M7:** reusable details/native actions, transaction reconciliation, repeatable deltas, trader full workspace, relevant items/Wiki/Flea, and final interaction polish. Complete and accepted.
- **M8:** stabilization, multi-map/task-scope finalization, compatibility fixes, performance, validation, and release hardening. Complete and user-accepted on 2026-08-19; `ingame-ui` is merged into `main`.

## Superseded designs — do not resurrect

- MVC/monolithic HTML browser app, physical `wwwroot`, browser JSON topology/profile APIs, or deployed `.js`/`.ts` files.
- `QuestHelper.GetClientQuests` cloning/mutation path.
- Client-side condition-type/location inference, raid-entry `TriggerWithId` scans, or copied Factory/Ground Zero alias constants.
- `MainMenuControllerClass.ShowScreen` Harmony patch.
- The retired M03 trader controller, read-only future pane, `QuestGraphDataSet`, or separate node/edge renderers.
- Native Tasks/Return-to-Map escape hatch, repurposed native regular/daily selectors, route filters, or the removed Tasks status filter.
- Per-view selection persistence or stale selection resurrection.
- Full-screen/table teardown for sorting, filtering, completed-task toggles, or one-row expansion.
- Synchronous native handover eligibility checks for every table objective.
- Full active-quest `Progress.GetHashCode()` polling.
- Full-row hover highlight setting, redundant task-row selection tooltip, or custom task-list double-click setting.
- A separate `EnableCustomQuestDetails` switch; details are intrinsic to an enabled replacement.
- Recursive Lightkeeper-path satisfaction as trader availability; only Knock-Knock success is the unlock requirement.
- Direct profile/counter mutation or fabricated live quest instances.

## Reference documents

Use these for detail rather than expanding this status again:

- `AGENTS.md` — repository working rules.
- `docs/in-game-client-design.md` — exact client targets, signatures, hashes, and hierarchy evidence.
- `docs/acceptance.md` — acceptance matrix/history.
- `docs/quest-actions-plan.md` — server-only action design audit; native client now owns actual live actions.
- `docs/ingame-map/M06-native-web-parity-audit.md` — native/browser parity decisions.
- `docs/ingame-ui-branch-changes.md` — exhaustive player-facing branch summary.
- M8 plan/checklist documents — closed 2.0 release-hardening record.

Historical implementation narratives, old release hashes, PIDs, failed candidates, and superseded UI geometry belong in Git history or the archived long status, not in this operational handoff.

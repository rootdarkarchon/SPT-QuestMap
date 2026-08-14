# SPT-QuestMap

SPT-QuestMap 2.0 is a **full in-game questing replacement** for **SPT 4.0.13**. It replaces both the global **Character → Tasks** screen and every trader's **Tasks** tab with a complete quest workspace for browsing, filtering, accepting, progressing, tracking, handing in, and planning quests without leaving EFT.

This is not just a quest-map overlay or an extra details panel. The native client provides its own full task table, dependency graph, quest details, native quest actions, tracking system, and in-raid overlays. A read-only browser companion at `/questmap` adds large-screen planning and profile comparison.

> [!WARNING]
> ## This project is AI-coded
>
> Most of SPT-QuestMap was written by **OpenAI Codex**, starting from detailed human direction and continuing through many rounds of hands-on review, visual feedback, testing, and correction. It is not a traditionally authored or independently audited codebase.
>
> The browser/server map is deliberately read-only. The in-game client has an optional objective-skip action which is disabled by default; keep profile backups before enabling it. You should still treat the complete mod like any other community tool: review releases before installing them and report anything that looks wrong. AI-generated code can contain convincing mistakes.

SPT-QuestMap is an independent community project. It is not affiliated with or endorsed by Battlestate Games or the SPT project.

## At a glance

| Surface | Role | Main features |
| --- | --- | --- |
| Native client | The primary, full-featured questing replacement inside EFT | Quest table, dependency map, complete details, native quest actions, tracking, raid progress notifications, and tracked-quest list |
| Browser companion | A read-only planning and comparison view at `/questmap` | Large interactive graph, profile selection and comparison, progression analysis, search, filters, and quest details |

The combined release installs both parts. The dependency is one-way: the **server component can run by itself** and is all that is required for the browser-based QuestMap, but the **native client requires the QuestMap server component** for its topology and repeatable-quest data. They share the same understanding of quest state, progress, blockers, dependencies, routes, repeatables, and locations, so the in-game workspace and browser planner remain consistent.

## Feature overview

- **A complete in-game quest workspace.** Browse every relevant quest from the global or trader Tasks screens, sort and filter the list, inspect progress, and switch directly to a visual quest map. Your selected view, filters, sorting, quest, and graph position are retained as you move through the menus.
- **The full quest lifecycle.** Accept available quests, restart eligible failures, hand over items and currency, turn in completed quests, and replace repeatables using EFT's familiar confirmations and reward screens. Changes appear throughout the workspace immediately.
- **Quest progression you can understand.** See what is available now, what is active or ready to turn in, what failed or became excluded, and exactly what blocks future quests—including level, trader, loyalty, reputation, prerequisite, and wait-time requirements.
- **A real dependency map, not a flat list.** Explore prerequisite chains, future unlocks, mutually exclusive branches, cross-trader progression, Collector and Lightkeeper routes, and end-of-line quests. Focus on one chain or reveal the complete future graph when planning ahead.
- **Detailed quest guidance.** View ordered objectives and live progress, descriptions and localized summaries, rewards and penalties, actual objective locations, Wiki links, and relevant quest items such as required keys and Gunsmith equipment.
- **Built for active play.** Manually track quests, inherit tracking from favorites or the current raid map, receive in-raid objective progress notifications, and press a configurable hotkey for a compact tracked-quest list grouped by trader.
- **Daily, Weekly, and Scav Daily support.** Repeatables have their own sections and map bands, timers, status presentation, localized generated objectives, replacement actions, and explicit Scav identification.
- **Powerful search and filtering.** Narrow quests by name, trader, status, location, level eligibility, repeatable type, completion state, or route without losing the selected quest's progression context.
- **A browser companion for deeper planning.** Open the same quest graph on a larger screen, inspect any loaded profile without modifying it, or compare two profiles to see status, objective, branch, wait-time, and profile-exclusive differences.
- **A polished replacement rather than a disposable overlay.** QuestMap uses EFT's artwork and interface language, provides native-style feedback and tooltips, remembers where you left off, and lets you independently restore either vanilla Tasks surface from F12.
- **Optional task skipping, kept separate and safe by default.** Objective-only skipping requires explicit opt-in, a held modifier, and confirmation; it is unavailable in raid and never turns in the quest or hands over items.

## Compatibility

This release targets **SPT 4.0.13** and EFT build **0.16.9.40087 exactly**. It was built and tested against the matching server source, server assemblies, and client ABI. Do not assume it is compatible with SPT 4.1 or later.

The native client validates the installed identity and required UI/action targets before activating. A mismatch safe-disables the custom screens and leaves the vanilla interfaces available instead of guessing at a changed ABI.

When either in-game Tasks replacement is enabled, QuestMap renders a completely custom task table rather than extending EFT's native task-list rows. Mods that patch or decorate the native list—most notably **DrakiaXYZ Quest Tracker** and **Task List Fixes**—cannot apply their task-list integration to QuestMap's replacement. QuestMap provides its own profile-scoped pin/tracking controls, in-raid progress notifications, tracked-quest overlay, sorting, filtering, and corrected task presentation; the corresponding Quest Tracker and Task List Fixes behavior is therefore largely redundant. Do not expect those mods' native-list additions to appear inside QuestMap.

## Installation

The combined archive is the recommended installation. For a server/browser-only setup, install only `SPT/user/mods/SPT-QuestMap/`; the BepInEx client is not required. A client-only installation is not supported: `BepInEx/plugins/SPTQuestMap/` requires the matching server component to be installed and running.

1. Back up the profiles you care about.
2. Stop the SPT server and close EFT.
3. Download the combined release archive and extract it into the Tarkov installation directory containing `SPT`, `BepInEx`, and `EscapeFromTarkov.exe`. The archive already includes both complete install-relative paths.
4. Allow the archive's `SPT` and `BepInEx` folders to merge with the existing ones and overwrite an older QuestMap release. Confirm the main runtime files exist:

   ```text
   <Tarkov root>/SPT/user/mods/SPT-QuestMap/SPTQuestMap.dll
   <Tarkov root>/SPT/user/mods/SPT-QuestMap/SPTQuestMap.Core.dll
   <Tarkov root>/SPT/user/mods/SPT-QuestMap/Data/metainfo.json
   <Tarkov root>/SPT/user/mods/SPT-QuestMap/Data/triggerIds.json
   <Tarkov root>/BepInEx/plugins/SPTQuestMap/SPTQuestMap.Client.dll
   <Tarkov root>/BepInEx/plugins/SPTQuestMap/SPTQuestMap.Core.dll
   ```

5. Start the SPT server and let it finish loading before starting EFT.
6. Open `/questmap` on the address used by your SPT server. With the common local configuration this is:

   ```text
   https://127.0.0.1:6969/questmap
   ```

Your browser may warn about SPT's local TLS certificate until you trust that certificate on your machine. Existing custom files beneath `SPT/user/mods/SPT-QuestMap/summaries/` are user-managed and are not part of the release archive.

## Using QuestMap in EFT

When enabled, QuestMap owns the complete questing experience on these screens: the quest list, map, details, actions, filtering, sorting, selection, and navigation all belong to one integrated workspace. EFT's native Notes and Quest Items views remain available alongside it.

The two replacement surfaces are enabled by default and can be changed independently in the F12 configuration manager:

- **Replace global Tasks screen** changes the quest portion of **Character → Tasks** while retaining EFT's native Notes and Quest Items views.
- **Replace trader task screens** changes each trader's Tasks tab.

Turning off either setting restores that complete vanilla surface.

### Global Tasks

The global screen has **Tasks** and **Quest Map** views alongside the retained native **Notes** and **Quest Items** views.

The Tasks view provides:

- sortable Quest, Trader, Location, Status, Progress, and Tasks columns;
- natural numeric ordering for quest series;
- quest/trader search plus trader, status, map, and repeatable filters;
- separate pinned, Daily, Scav Daily, Weekly, and ordinary sections;
- native favorite stars and a separate QuestMap tracking control;
- current/required objective progress while an objective remains incomplete; and
- Accept, Restart, Hand In, Turn In, and repeatable Replace actions only when they are actually eligible.

Map filters support normal multi-selection. Right-click a map to make it the exclusive map filter. The retained table preserves its filters, sort, selection, scroll position, rows, and cached artwork when you leave and reopen Tasks.

The Quest Map view supplies search and trader/status/level/repeatable filters, future-depth and finished-quest toggles, drag panning, cursor-centered wheel zoom, Fit, Center, Focus Chain, and selection controls. During a raid, the global replacement intentionally remains on Tasks and does not offer the full dependency graph.

The native Quest Items button displays a warning and count when the character is carrying quest raid items.

### Trader Tasks

Each trader receives the same **Tasks** and **Quest Map** workspace, scoped to that trader's useful quest context. The ordinary trader view includes one future tier; selecting a quest can reveal its prerequisite and downstream chain across other traders.

Search, filters, sorting, selected view, graph viewport, and selected quest are retained per profile and trader. Unavailable quests are hidden by default and can be shown. The trader table intentionally omits the global-only favorite column.

### Details and native quest actions

Selecting a quest opens one details pane with fixed quest identity/action headers and a single scrolling body. Depending on the quest, it includes Description, Summary, Relevant Items, Objectives, and Rewards.

QuestMap uses EFT's initialized quest controllers and confirmation/reward flows for ordinary actions:

- accept an available quest;
- restart an eligible failure;
- hand over ordinary items, partial stacks, currency, or weapon assemblies;
- turn in a completed quest; and
- replace an eligible repeatable quest.

The affected rows, graph cards, details, blockers, and newly unlocked quests refresh after an action. QuestMap does not inject fake future quests into EFT's live quest book or implement a parallel inventory/profile transaction system.

Relevant-item entries show `(Have: N)`. Flea-eligible entries can open EFT's native **Filter by item** search outside raids; Flea navigation and other menu-only quest actions are disabled in raid. Wiki links remain available in raid and open in the default Windows browser.

### Tracking and raid overlays

QuestMap tracking is separate from EFT's native favorite flag:

- Click a quest's **Status** cell to add or remove manual tracking.
- Native favorites are tracked implicitly by default.
- Active quests assigned to the current raid map are tracked implicitly by default. Genuine `Any` quests are not automatically included by this policy.
- Newly accepted quests are not automatically added to manual tracking unless that option is enabled.

Manual tracking is stored per profile under QuestMap's BepInEx configuration data. Removing manual tracking does not hide a quest that is still implicitly tracked because it is a favorite or applies to the current map.

Tracked quests can produce stacked, deduplicated in-raid progress notifications. Press **I** by default to show or hide the compact tracked-quest list for the current raid. Smart in-raid tracking omits objectives that are only useful in menus, such as trader hand-ins, weapon assembly, loyalty, standing, skill, and hideout requirements.

### Important F12 defaults

| Setting | Default | Effect |
| --- | ---: | --- |
| Replace global Tasks screen | On | Enables the Character → Tasks replacement |
| Replace trader task screens | On | Enables trader Tasks replacements |
| Show hidden quest rewards | On | Includes template rewards marked hidden |
| Prefer quest summary | On | Opens Summary first when one is available |
| Enable task skipping | **Off** | Allows modifier-revealed objective skipping |
| Task skip modifier | Left Ctrl | Hold to reveal eligible **SKIP** controls |
| Track newly accepted quests | Off | Adds new accepts to manual tracking |
| Track favorite quests | On | Treats EFT favorites as implicitly tracked |
| Track quests for current map | On | Implicitly tracks active map-relevant quests |
| Smart in-raid tracking | On | Removes menu-only objectives from the raid list |
| Tracked quest list hotkey | I | Toggles the raid quest list |
| Minimal progress notifications | Off | Uses the full notification presentation |
| Detailed diagnostics | Off | Enables verbose lifecycle/performance logging |

Notification timing and opacity, interface sounds, and highlight/state/route colors are also configurable. Warnings and errors are logged even when detailed diagnostics are disabled.

### Optional task skipping

Task skipping is implemented directly against the guarded EFT `0.16.9.40087` quest-condition controller; SPT-Skipper is not required.

Enable **Quest actions → Enable task skipping** in F12, then hold the configured **Task skip modifier** (Left Ctrl by default). An eligible incomplete objective shows **SKIP** in the Tasks table and Quest Description. The native confirmation names the quest and objective before QuestMap changes only that objective.

Task skipping:

- is unavailable during raids;
- does not hand in items;
- does not complete or turn in the whole quest; and
- does not create a standalone server transaction.

Because of that last boundary, task-only progress is not guaranteed to survive a complete client/server reload before the quest proceeds through its normal turn-in flow. Keep the setting off unless you deliberately want this behavior.

## Using the browser map

- Pick a profile in the top bar. Headless and level-zero profiles are intentionally omitted.
- Use the compare control to add profile B. Matching quests collapse to one dimmed card, while changed quests show an outlined reason badge and explicit A/B state or progress rows. The comparison bar can show all quests, all changes or one change category while preserving selected-chain context. Profile-generated repeatables are hidden during comparison because their profile-local IDs cannot be matched safely.
- Click a quest to inspect its details and highlight its recursive prerequisites and direct successors.
- Selection behaves the same with or without a trader filter: recursive prerequisites remain visible across traders, while only direct successors are highlighted.
- Double-click a quest, or select it and press **Focus chain**, to compact the view to that chain.
- Use **Show All Future Quests** when you want the complete future graph instead of the next useful frontier.
- Toggle finished quests or level-ineligible quests depending on how much context you want.
- Search by quest, trader, or ID, or select a trader portrait to show that trader's filtered quests plus one tier after its currently available, active, or completed quests. Successors may belong to other traders, while the finished and level-eligibility filters still apply.
- Trader-filtered prerequisite-gated quests also show each direct prerequisite that is still unmet, even when that prerequisite belongs to another trader.
- Open **Quests In Progress** on the left for a filter-independent progress list. Selecting an entry also selects and centers it in the graph.
- Daily, Weekly, and Scav Daily operational quests appear above the graph in trader order. Scav repeatables are explicitly marked on their band, quest cards, task rows, and details banner. Available, accepted, ready-to-finish, completed, and expired entries use the same state styling and details panel as ordinary quests; trader, search, and finished filters also apply. A default-on calendar filter toggles the complete band.
- Press the circular refresh button after changing profile progress in-game. QuestMap never refreshes automatically.

## Custom quest summaries

QuestMap includes English and Russian summaries for the standard quest catalog. Add summaries for modded or overridden quests beneath the server mod directory:

```text
SPT/user/mods/SPT-QuestMap/summaries/
```

Each top-level JSON file is an object whose keys are quest IDs and whose values are summary strings. A file may contain quests from any trader:

```json
{
  "0123456789abcdef01234567": "A concise localized summary."
}
```

Name `<catalog>.json` as English or `<catalog>.<language>.json` for another SPT language code. German, for example, uses `ge`:

```text
my-quest-pack.json
my-quest-pack.ge.json
```

Files load in deterministic filename order, and a later file overwrites an earlier value for the same quest and language. Summary resolution order is:

1. requested custom language;
2. custom English;
3. any available custom language; and
4. the embedded standard catalog.

Malformed optional files are logged and skipped. A missing summary simply leaves the ordinary Description view without Summary tabs. Restart the SPT server after adding or changing catalogs. QuestMap's deployment helper preserves this directory.

## Local-server warning

SPT 4.0.13 does not provide an authentication policy for this mod's Blazor page. QuestMap therefore assumes the normal SPT setup where the web server is bound to localhost. If you expose the SPT web host to another machine or network, the QuestMap page and its sanitized profile/quest state become reachable there too.

## Troubleshooting

- **`/questmap` does not open:** confirm that the SPT server finished loading, use the server's configured HTTPS address, and account for its local TLS certificate.
- **EFT still shows the vanilla task screen:** check both F12 replacement toggles and `BepInEx/LogOutput.log`. An exact-build or required-target mismatch intentionally disables the native replacement.
- **Another task mod's row control is missing:** this is expected while QuestMap owns that replacement surface; disable the corresponding QuestMap toggle to restore native rows.
- **QuestMap UI text falls back to English:** the native client uses a matching embedded language catalog when present and falls back to English per key.
- **A quest state looks stale in the browser:** use the visible Refresh control. Browser QuestMap deliberately does not poll.
- **A custom summary is ignored:** keep it directly inside lowercase `summaries/`, validate the JSON object shape, and restart the server.
- **More diagnostics are needed:** enable **Diagnostics → Enable debug logging** in F12 and reproduce once; ordinary warnings and errors never require that toggle.

## Building from source

You need:

- the .NET 9 SDK;
- a matching SPT 4.0.13 installation containing `SPT`, `BepInEx`, `EscapeFromTarkov.exe`, and the target client assemblies; and
- the matching SPT 4.0.13 source under `reference/spt-4.0.13-sources/` when auditing or changing SPT integration behavior.

SPT, EFT, Unity, and BepInEx assemblies remain external read-only references and are not committed or redistributed.

Pass the Tarkov installation directory—the parent of `SPT`, not the `SPT` directory itself—to the build script:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 `
  -Target Both -Configuration Release -SptRoot 'D:\path\to\Tarkov-SPT'
```

The combined build validates SPT 4.0.13, builds the server and client, runs the shared-core and server/browser tests, and stages clean outputs under `dist/client/` and `dist/server/`.

To deploy a validated combined build to that installation:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy.ps1 `
  -Target Both -Configuration Release -SptRoot 'D:\path\to\Tarkov-SPT'
```

The deployment helper limits writes to the two QuestMap install directories. Client deployment does not inspect or stop EFT. If the server DLL changes and the exact configured SPT server is running, the helper restarts only that executable; an explicit `-RestartCommand` is available for other environments.

After building, create an install-ready combined archive with the version declared by all three projects:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 `
  -Target Both -Configuration Release -Version 2.0.0
```

See the [script reference](scripts/README.md), [deployment contract](docs/deployment.md), and [testing guide](docs/testing.md) for the complete workflow.

## Architecture and project status

The implementation is split into:

- `SPTQuestMap`: the .NET 9 SPT server mod, Blazor page, profile projection, localization, and sanitized client feeds;
- `SPTQuestMap.Core`: runtime-neutral topology, state, progress, visibility, layout, routing, and tracking rules shared by server and client; and
- `SPTQuestMap.Client`: the exact-ABI `netstandard2.1` BepInEx/Unity client.

Start with the [architecture overview](docs/architecture.md), [project layout](docs/project-layout.md), and [native client design](docs/in-game-client-design.md). The [development status](docs/development-status.md) records implementation history, automated evidence, deployment evidence, and remaining live-game checks separately.

## Attribution and acknowledgements

The relevant-item data for Gunsmith quests and quest-required keys was derived from the quest data distributed with SPT and information from the [Escape from Tarkov Wiki](https://escapefromtarkov.fandom.com/wiki/Escape_from_Tarkov_Wiki), then assembled in QuestMap's `metainfo.json` format. The idea to surface this information alongside quest text was inspired by [CJ-SPT's Expanded Task Text](https://github.com/CJ-SPT/Expanded-Task-Text); its data is not the source of QuestMap's catalog.

QuestMap's quest-tracking functionality and in-raid tracked-quest display were inspired by [DrakiaXYZ's SPT Quest Tracker](https://github.com/DrakiaXYZ/SPT-QuestTracker). QuestMap's implementation was written independently for its custom task tables and data model; it does not incorporate Quest Tracker source code.

QuestMap's Wiki-link additions were inspired by [Tyfon's WikiLinks](https://github.com/tyfon7/WikiLinks). WikiLinks source code was not referenced while implementing the feature, and no WikiLinks code is incorporated into QuestMap.

The objective-skip mechanism was source-matched against [acidphantasm's SPT-Skipper 1.1.4](https://github.com/acidphantasm/SPT-Skipper/tree/1.1.4). QuestMap reimplements the narrow Tarkov controller operation for its custom task rows and does not incorporate SPT-Skipper as a dependency.

## License

SPT-QuestMap is available under the [MIT License](LICENSE).

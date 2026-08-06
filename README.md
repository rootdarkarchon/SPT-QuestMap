# SPT-QuestMap

SPT-QuestMap is a read-only, profile-aware quest map for **SPT 4.0.13**. It runs inside the existing SPT server and turns the game's quest data and your selected profile into an interactive dependency graph at `/questmap`.

> [!WARNING]
> ## This project is AI-coded
>
> Most of SPT-QuestMap was written by **OpenAI Codex**, starting from detailed human direction and continuing through many rounds of hands-on review, visual feedback, testing, and correction. It is not a traditionally authored or independently audited codebase.
>
> The mod is deliberately read-only, but you should still treat it like any other community tool: keep profile backups, review releases before installing them, and report anything that looks wrong. AI-generated code can contain convincing mistakes.

SPT-QuestMap is an independent community project. It is not affiliated with or endorsed by Battlestate Games or the SPT project.

## What it does

- Reads the quests and profiles already loaded by your SPT server.
- Shows completed, active, ready-to-finish, available, failed, excluded, pending, and gated quests.
- Explains effective blockers such as player level, trader availability, loyalty level, standing, and prior quests.
- Displays ordered objectives and known progress, including partial counter progress.
- Shows quest descriptions, locations, rewards, prerequisites, and direct successors.
- Marks Collector and Lightkeeper routes.
- Uses the quest, trader, item, and location artwork already served by SPT; no game artwork is copied into the mod.
- Filters automatically for faction and active seasonal/event applicability.
- Keeps the default graph focused on known quests plus the immediate future tier, with an option to reveal all future quests.
- Provides search, trader, level-eligibility, and finished-quest filters.
- Includes a persistent **Quests In Progress** drawer grouped by trader.
- Places profile-generated Daily and Weekly operational quests in a labeled horizontal band above the dependency graph, including live remaining time and expired-state display.
- Preserves profile choice, language, filters, selection, focus state, pan, and zoom in the browser.
- Compares two profiles on one shared graph with explicit change categories, readable A/B transitions, objective deltas, profile-exclusive quests and category filters.
- Uses a Canvas renderer so the full graph remains practical to pan and zoom.
- Refreshes only when you ask it to. It does not poll or modify the profile.

## Compatibility

This release targets **SPT 4.0.13 exactly**. It was built and tested against the matching 4.0.13 server source and assemblies. Do not assume it is compatible with SPT 4.1 or later.

## Installation

1. Stop the SPT server.
2. Download the release archive and extract it into the directory that contains your existing `SPT` folder. The archive already includes the complete `SPT/user/mods/SPT-QuestMap` path.
3. Allow the archive's `SPT` folder to merge with the existing one, then confirm the resulting path looks like:

   ```text
   <install parent>/SPT/user/mods/SPT-QuestMap/SPTQuestMap.dll
   <install parent>/SPT/user/mods/SPT-QuestMap/SPTQuestMap.Core.dll
   ```

4. Start the SPT server and let it finish loading.
5. Open `/questmap` on the address used by your SPT server. With the common local configuration this is:

   ```text
   https://127.0.0.1:6969/questmap
   ```

Your browser may warn about SPT's local TLS certificate until you trust that certificate on your machine.

## Using the map

- Pick a profile in the top bar. Headless and level-zero profiles are intentionally omitted.
- Use the compare control to add profile B. Matching quests collapse to one dimmed card, while changed quests show an outlined reason badge and explicit A/B state or progress rows. The comparison bar can show all quests, all changes or one change category while preserving selected-chain context. Daily and Weekly quests are hidden during comparison because separately generated quests cannot be matched safely.
- Click a quest to inspect its details and highlight its recursive prerequisites and direct successors.
- Selection behaves the same with or without a trader filter: recursive prerequisites remain visible across traders, while only direct successors are highlighted.
- Double-click a quest, or select it and press **Focus chain**, to compact the view to that chain.
- Use **Show All Future Quests** when you want the complete future graph instead of the next useful frontier.
- Toggle finished quests or level-ineligible quests depending on how much context you want.
- Search by quest, trader, or ID, or select a trader portrait to show that trader's filtered quests plus one tier after its currently available, active, or completed quests. Successors may belong to other traders, while the finished and level-eligibility filters still apply.
- Trader-filtered prerequisite-gated quests also show each direct prerequisite that is still unmet, even when that prerequisite belongs to another trader.
- Open **Quests In Progress** on the left for a filter-independent progress list. Selecting an entry also selects and centers it in the graph.
- Daily and Weekly operational quests appear above the graph in trader order. Available, accepted, ready-to-finish, completed, and expired entries use the same state styling and details panel as ordinary quests; trader, search, and finished filters also apply. A default-on calendar filter toggles the complete band. Scav dailies are intentionally excluded.
- Press the circular refresh button after changing profile progress in-game. QuestMap never refreshes automatically.

## Local-server warning

SPT 4.0.13 does not provide an authentication policy for this mod's Blazor page. QuestMap therefore assumes the normal SPT setup where the web server is bound to localhost. If you expose the SPT web host to another machine or network, the QuestMap page and its sanitized profile/quest state become reachable there too.

## Building from source

You need the .NET 9 SDK and a matching SPT 4.0.13 installation. The SPT assemblies remain external references and are not committed to this repository.

```powershell
dotnet test .\src\SPTQuestMap\SPTQuestMap.slnx -c Release -p:SptInstallRoot="D:\path\to\your\SPT-install-parent"
```

`SptInstallRoot` is the directory containing the `SPT` folder. See [build and deployment notes](docs/deployment.md) for the guarded deployment flow, and [architecture](docs/architecture.md) for the Blazor/Canvas boundary.

The product requirements and implementation record remain available under [`docs/`](docs/). They are useful for contributors, but ordinary users do not need them to install the mod.

## License

SPT-QuestMap is available under the [MIT License](LICENSE).

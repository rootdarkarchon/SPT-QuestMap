# QuestMap 2.1 / SPT 4.1 migration

## Target and evidence

- Runtime admission: SPT `>=4.1.6 <4.2.0`, EFT build `40743`, the known game assembly fingerprint, and all required native member contracts. Later admitted patches are not claimed as live-tested.
- Build baseline: installed SPT 4.1.6 / EFT `0.16.9.40743`; server source commit `731d7a2a4418865c37141862ec5ad2451525f7f8` from `SP-Tushonka/server-csharp`.
- Matching source: ignored `reference/spt-4.1.6-sources/`. Its default version is overridden by the official build; the installed informational version identifies the commit.
- Class mapping: `E:\Downloads\4.1 mappings.txt`, verified against installed metadata. No installed SPT assembly was decompiled.
- Game assembly SHA-256: `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.
- Server Core SHA-256: `490409F7C67480A8BF647DA67ADA8B91361330C2A9324D9E9787CD1EBAF5BF27`.

QuestMap 2.0 releases retain SPT 4.0.13 support. This tree has no dual-runtime implementation. All three production projects and the plugin use version 2.1.0.

## Server migration

The server/tests use .NET 10; shared core and native client remain `netstandard2.1`. `IModMetadata` and `IModBlazorMetadata` replace the old metadata contracts and register `/questmap` as a homepage. Embedded CSS/Canvas assets and the existing Blazor host remain in use.

Injected `TemplateTable`, `TradersTable`, `LocationTable`, and `LocaleTable` replace `DatabaseService`. Locale contents still go through `LocaleService`. Preload runs at `OnLoadOrder.PostLoad + 1`; metadata initialization follows at `+ 2`. Both implement `OnLoadAsync(CancellationToken)`. Cancellation prevents publishing incomplete preload results. Loose-loot materialization remains a one-time startup operation even with uncached 4.1 lazy values; profile/repeatable requests reuse the preload.

Availability uses the matched `QuestHelper` edition blacklist/whitelist helpers for quests absent from the profile. Profile-known statuses retain precedence. Edition-inapplicable future quests are excluded from profile applicability/totals. QuestMap does not call `GetClientQuests`, which stamps shared template state. Existing faction, seasonal, Block, prestige, frontier, dependency, and task-map rules remain in place.

The browser page uses a QuestMap-specific policy that requires an authenticated SPT user by default, including non-administrators. Server `config.json` can explicitly allow anonymous access with `requireBrowserAuthentication: false`. SPT still controls cookies, login, and optional localhost bypass; other host policies are unchanged. Native feeds retain existing session transport and URLs. No browser write endpoints or raw-profile payloads were added.

## Native migration

The ten mapped types now use named 4.1 namespaces: Quest, QuestController, QuestControllerClient, DailyQuest, IEftSession, Trader, FavoriteQuestManager, RagfairSearch, LocalizationManager, and EftScreenManager.

Updated bridges include `ConditionsConnectorsManagerClient`, `_favoriteQuests`, `LocalizationManager.Instance.Culture`, `Player.QuestController`, direct condition collection enumeration, and `QuestObjectiveView.QuestHandover(Quest)`. The shared `QuestReward` is explicitly distinguished from EFT's new type.

`NativeTargetContract` records 71 required signatures, fields, properties, and events. The same reflection validator runs in the client and metadata-only tests. It covers all private-field bridges, native quest action hosts, completion/reset, and monitoring events. Exactly four Show/Close lifecycle methods are patched. Unknown fingerprints or contract mismatches prevent runtime initialization and leave vanilla surfaces available, with unconditional diagnostics.

Native action ownership, duplicate-press protection, hidden transaction hosts, and optional checker-based skipping remain in place. Configuration keys, tracking paths, favorites, UI persistence, cached topology, incremental rows, bounded polling, and asset concurrency are retained.

## Validation and remaining acceptance

The unchanged client initially produced 145 compilation errors. The migrated combined Release build has zero warnings/errors and passes 103 core, 189 server/browser, and 22 client compatibility tests (314 total). These include edition filtering/all ten statuses, injected-table preload/cancellation/order, configurable browser access and authenticated defaults, unauthorized route rendering, range/fingerprint rejection, missing members/changed signatures, and SPT serialization of kill classification, progress, and repeatable deadlines.

Live acceptance remains pending:

1. Server startup, `/questmap`, localhost/remote authentication, profile selection/comparison, images, refresh, and read-only behavior.
2. Global/trader Tasks; accept/restart/hand-in/turn-in/replace; optional skip/reset recovery; favorites, tracking, localization, notes/quest items, and disable-to-vanilla behavior.
3. Standalone raid transitions; notifications at 0/10-second kill delay; FIR high-water marks; new-raid defaults; post-raid and manual topology reloads.
4. Daily/Weekly/Scav expiry labels in banners and details, including narrow columns and Russian text.
5. Fika handover/turn-in and post-raid hidden-host lifecycle on explicitly recorded current Fika versions. Prior 4.0 observations do not establish 4.1 compatibility.
6. Full/modded-graph startup, request timings, first Tasks opening, pan/zoom, and raid monitoring. Investigate order-of-magnitude regressions or visible first-open stalls; no new live performance measurements are claimed.

See [development status](development-status.md) for package/deployment evidence and [deployment](deployment.md) for the configured restart command and user-provisioned private CI inputs.

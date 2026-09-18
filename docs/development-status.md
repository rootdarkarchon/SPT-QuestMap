# Development status — current operational handoff

Last consolidated: 2026-09-18. Update this file in place; keep historical implementation details in Git history. The archived status is immutable.

## Status at a glance

- **Release candidate:** QuestMap `2.1.2`, complete server/browser/core/native migration to SPT 4.1. The accepted 2.0 feature set is retained; prior releases provide 4.0.13 support.
- **Automated validation:** combined Release build succeeded with zero warnings/errors; **314 tests passed**: 103 core, 189 server/browser, 22 native compatibility. The old 273-test count is historical.
- **Acceptance:** not yet accepted on the live 4.1 installation. No deployment, server restart, live browser/game/Fika validation, or new runtime performance measurements occurred.
- **Blocker:** deployment requires the user's configured server restart command. No command is configured or inferred. CI hosting/secrets will be provisioned by the user.
- **Next step:** deploy the validated combined package after the restart command is supplied, then complete the live matrix in [SPT 4.1 migration](spt-4.1-migration.md) and [acceptance](acceptance.md).

## Runtime and integration contracts

- Build target: `D:\Tarkov-SPT-4.1`, SPT **4.1.6**, EFT **0.16.9.40743**.
- Permitted SPT range: **>=4.1.6 <4.2.0**, only with the known EFT fingerprint and all required native member contracts. Later patches are permitted conditionally, not claimed as tested.
- Matched server source: `731d7a2a4418865c37141862ec5ad2451525f7f8`, in ignored `reference/spt-4.1.6-sources/`. Vendor sources and installed assemblies remain unmodified; no SPT DLL was decompiled.
- EFT assembly SHA-256: `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.
- Server Core SHA-256: `490409F7C67480A8BF647DA67ADA8B91361330C2A9324D9E9787CD1EBAF5BF27`.
- Server and all test projects target `net10.0`; core/client remain `netstandard2.1`. All production project/plugin versions are `2.1.2`.
- `IModMetadata` / `IModBlazorMetadata` register `/questmap`; `HasPrepatcher = false`. Embedded Razor, CSS, and Canvas assets remain in use.
- The browser header reads `ProgramStatics.SPT_VERSION()` from the running server, matching SPT's own status page; it no longer displays a hardcoded version.
- `TemplateTable`, `TradersTable`, `LocationTable`, and `LocaleTable` replace `DatabaseService`. Cancellation-aware preload runs at `PostLoad + 1`, catalog initialization at `+ 2`.
- The all-profile page uses the `QuestMapBrowser` policy: any authenticated SPT user is allowed by default, without an administrator requirement. The package ships `config.default.json`; startup creates active `config.json` beside the server DLL with `requireBrowserAuthentication: true` if missing. Explicitly set it to `false` in `config.json` and restart to allow anonymous browser access. Invalid settings retain authentication. SPT owns login, cookies, and configured localhost bypass; other host policies and native feed session authentication are unchanged.
- Native integration uses the supplied 4.1 mappings and installed named metadata. **71 required member contracts** are validated before activating replacements; exactly four global/trader Show/Close targets are patched. Unknown fingerprints or missing contracts leave replacements disabled with diagnostics.

## Preserved behavior and authority boundaries

- The browser receives sanitized, read-only snapshots through Blazor. Do not expose raw profiles or add write endpoints.
- Profile-known quest statuses take precedence. Future availability uses matched faction, edition allowlist/denylist, seasonal, level, trader, and prerequisite checks. Keep external Block/prestige gates and deterministic duplicate handling.
- Never call mutating `QuestHelper.GetClientQuests` for browser projection or `QuestBook.LoadAll()` to fabricate live client quests.
- Quest acceptance, restart, handover, completion, replacement, rewards, and optional skipping remain owned by initialized EFT views/controllers/checkers. Preserve transaction reconciliation, duplicate-press protection, hidden-host lifetime, reset recovery, and raid restrictions.
- Keep objective dependency order, authoritative task locations/relevance, canonical map aliases, Arena exclusion, and native availability overrides. Do not infer task scope on the client or rewrite native `Quest.Location`.
- Preserve global/trader Tasks, Quest Map, details, notes/quest items, favorites, tracking, comparison, localization, summaries, and UI persistence. Configuration keys and tracking paths are unchanged.
- All 558 quest Wiki links in `Data/metainfo.json` are pinned with `?oldid=` to the latest revision at or before **2025-11-14T23:59:59Z**. Fandom API revision timestamps were verified for all 552 distinct URLs, including 90 titles recovered through page-move logs; original URL paths and all other metadata are unchanged. The four existing metadata catalog tests pass. This data update is included in the current package but has not been deployed.
- Kill-notification delay (0–10 seconds), FIR high-water behavior, repeatable countdown banners/details, and post-raid refresh are retained but still need live 4.1 acceptance.
- Daily/Weekly/Scav Daily banner and detail timers use localized `Expires in hh:mm:ss`, adding `Xd` only above 24 hours. English and Russian catalogs include the complete phrase; labels update each second and retain localized Expired at zero. Formatting checks covered 1 second, either side of 24 hours, exactly 24 hours, and multiple days. Banner text uses available width without crossing the trader portrait; live layout acceptance is pending.
- Failed replacement initialization must restore vanilla UI. Native-row integrations remain available when the relevant replacement is disabled.
- Preserve user-managed summaries, settings, and shipped Data files during deployment. Never distribute vendor assemblies.

## Performance boundaries

- Materialize loose-loot/quest-item mapping once at startup, including uncached 4.1 lazy values. Reuse cached locations/indexes on topology, profile, and repeatable requests.
- Retain indexed graph propagation, deterministic cached layout, viewport culling/spatial hit testing, and topology-version invalidation when topology changes.
- Ordinary refreshes reuse topology/layout and apply profile/repeatable deltas. Sorting, filters, row expansion, and hover must not rebuild the full table or graph.
- Keep non-blocking shared main-menu warm-up, bounded polling, incremental raid monitoring, and the runtime-wide eight-request asset concurrency limit.
- Measure full/modded-graph startup, topology/profile requests, first Tasks opening, pan/zoom, and raid monitoring on 4.1. Historical 4.0 timings are not current acceptance evidence.

## Build and package evidence

```powershell
scripts/build.ps1 -Target Both -Configuration Release -SptRoot D:\Tarkov-SPT-4.1
scripts/package-release.ps1 -Target Both -Configuration Release -Version 2.1.2
```

- Build log: `artifacts/release-2.1.2-build.log` (fresh combined 2.1.2 build after the localized countdown update; zero warnings/errors, all 314 tests passed).
- Combined package: `artifacts/release/SPT-QuestMap-2.1.2.zip`.
- Package: **16 entries / 13 files, 852,353 bytes**; SHA-256 `3C1D3BEFE8C5010B6FEA824411B2BA072B6480EF7175DBE674ACAA53932B7A2A`.
- Inspection: zero escaping/unexpected entries, zero loose `.js`/`.ts`, and only four QuestMap DLL entries. `config.default.json` enables authentication; active `config.json` is excluded. Install roots are `SPT_Runtime/user/mods/SPT-QuestMap/` and `BepInEx/plugins/SPTQuestMap/`.
- `deploy.ps1 -Target Both -SkipBuild -WhatIf` confirmed destination paths; this was only a dry run.
- Tests cover injected tables, preload cancellation/order/single materialization, edition applicability, unchanged statuses, client-feed serialization, and deliberate ABI/version failures. Browser tests cover anonymous denial by default, ordinary-user access, explicit anonymous access, unchanged host administrator policy, and missing/invalid configuration. Deployment fixtures confirm preservation of `config.json` with or without a staged configuration, plus custom summaries. The compatibility project is registered in the solution.

## CI and rollout

- CI uses .NET 10 and one private, hash-pinned SPT 4.1.6 / EFT 40743 reference ZIP. The user will supply hosting and GitHub secrets.
- Private input: `artifacts/private-build-references/eft-spt-4.1.6-build-references.zip`, **30,507,998 bytes**, SHA-256 `2494CEA377BA01DAACEA78F7CCB17CFCF63DE9EBA19F2B34F2E87C867B63453E`; manifest beside it.
- Secret names: `EFT_REFERENCE_ARCHIVE_4_1_URL` and `EFT_REFERENCE_ARCHIVE_4_1_SHA256`. GitHub secret names cannot contain periods. Never attach this reference ZIP to a public QuestMap release.
- Deployment requires `-RestartCommand` or ignored `scripts/restart-command.local.txt` before replacing changed server DLLs. Invoke the supplied command after copying. Never infer a command, stop EFT, or poll readiness.
- Live validation must cover browser authentication (ordinary users, explicit anonymous mode, and host-configured bypass), all native actions, repeatable replacement/expiry, skip/reset recovery, localization/favorites/tracking, raid transitions, post-raid refresh, kill delay at **0 and 10 seconds**, and timers in both banners and details.
- Standalone and Fika lifecycle checks must pass before retaining a 4.1 Fika compatibility claim. Build/metadata success is separate from live-game evidence.

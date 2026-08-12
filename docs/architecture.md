# Architecture guidance

This is intentionally guidance rather than a mandatory class diagram.

## Primary principle

Keep static quest topology separate from profile-specific state.

Static topology changes when:

- server quest/config/locales change;
- mods alter quests;
- event configuration changes structurally.

Profile overlay changes when:

- a different profile is selected;
- the refresh button is pressed;
- active event state changes.

Do not recompute graph layout simply because quest statuses changed.

## Shared runtime-neutral core

`SPTQuestMap.Core` targets `netstandard2.1` and is consumed by both the net9.0 server/Blazor mod and the BepInEx client. It owns normalized topology/profile contracts, status and edge classification, comparison semantics, cycle-safe traversal, known-plus-frontier and event-descendant rules, deterministic layout, trader projection, spatial indexing, selection sets, and deterministic edge routes. It references neither runtime: no ASP.NET, Blazor, JavaScript, Unity, BepInEx, SPT, or EFT assemblies.

The server adapts SPT enums and DTOs at `Services/QuestGraphRules` and `Services/QuestProfileRules`; the client adapts live EFT objects in `EftLiveSnapshotAdapter`. Runtime-owned action, persistence, rendering, localization, and transport code remain outside the core. The server release therefore deploys `SPTQuestMap.Core.dll` beside `SPTQuestMap.dll`; SPT 4.0.13 loads both top-level assemblies and still finds exactly one `AbstractModMetadata` implementation.

The native client keeps `QuestMapDataRuntime` as its lifecycle-facing coordinator. `QuestRefreshCoordinator` owns observed-controller subscriptions, coalesced refresh ordering, topology/profile reconciliation, and targeted raid patches; `QuestScreenCoordinator` owns pending, mounted, suspended, and cached Tasks-screen controllers; `RaidQuestRuntime` owns raid entry/exit, progress monitoring, server-map-driven tracked-list projection, notifications, and raid telemetry; and `QuestActionReconciler` waits for native quest transactions to become observable before requesting a refresh. These components share the one `QuestMapDataAdapter` and `QuestTrackingService` created by the runtime, so the split does not duplicate topology, overlay, persistence, or event state. Raid entry no longer scans Unity `TriggerWithId` instances: canonical current-map IDs are compared directly with objective `MapIds` from topology.

The native client loads the complete localized topology once, then ordinarily reconciles profile state through the smaller locale-aware repeatable/profile feed. That feed carries the server's static topology version: a missing or mismatched version fails closed to the full topology route, while a match preserves the existing static topology and layout and patches only generated quests when their fingerprint changes. Inventory and trader-sales notifications use that small authoritative feed while a quest surface is visible; suspended surfaces remain unsubscribed. Visibility/membership projection is distinct from render geometry: table views never compact graph positions or materialize visible edge arrays, and state-only graph refreshes construct geometry only if membership changed. After reconciliation, only visible Tasks workspaces are refreshed; multiple changed quest IDs are applied as one batch so membership evaluation, viewport preservation, table layout, and selected-detail work are performed once per refresh. Native item handovers retain their hidden EFT objective host until the client quest state settles or the bounded transaction timeout expires; interim refreshes protect the affected row, and the final reconciliation forcibly rebuilds it.

The shared native sprite cache bounds local server asset transport to eight outstanding requests and applies at most one completed image decode/callback batch per frame. Successfully decoded sprites remain lifetime-cached: evicting and destroying them without ownership tracking could invalidate active Unity `Image` references, so memory-bounded eviction requires a separate reference-aware cache contract.

Within each mounted native Tasks workspace, `NativeQuestWorkspaceContext` owns the exact-version EFT session/controller binding, live quest lookup, raid mutation gate, action eligibility, and native quest transactions shared by the global and trader controllers. `NativeQuestViewHost` separately owns discovery, binding, and disposal of hidden EFT `QuestView` clones. The screen controllers retain their genuinely host-specific lifecycle, surface, filtering, selection, and persistence rules while composing the shared graph/table and detail views. `InProgressQuestTableView` and `QuestDetailsPane` remain responsible for their cohesive Unity rendering and local interaction state rather than fragmenting verbose UI construction into small indirection-only helpers.

## Server and Blazor responsibilities

- expose the mod-owned Razor page at `/questmap` through SPT's existing Interactive Server host;
- render semantic UI components and keep each browser circuit's page state isolated;
- load the embedded Canvas module and CSS from the mod assembly without adding public static-file routes;
- load sanitized profile summaries and normalized topology/profile snapshots directly through `QuestMapDataService`;
- provide existing asset URLs;
- enforce existing SPT web authentication/authorization.

A single combined endpoint is acceptable for the first implementation, but avoid sending full profile JSON.

## Canvas-module responsibilities

- graph layout and rendering;
- pan/zoom, hover, hit testing, and transient selection overlay;
- profile/status color overlay;
- image loading and caches;
- viewport persistence;
- low-frequency selection notifications to Blazor.

Blazor/C# owns filter and future-depth semantics, the objective/details panel, explicit refresh, and durable UI settings. On initialization and explicit data changes it passes one immutable topology/profile/view snapshot to the Canvas module. Browser viewport state remains independent and does not cross the circuit during interaction.

## Blazor-to-renderer snapshot

`QuestGraphSnapshot` contains the cached localized topology, selected profile overlay, and the small current `QuestGraphView`. It crosses JS interop only when the renderer initializes or the user explicitly refreshes/changes profile/language. Filter, focus, and selection changes send only `QuestGraphUpdate`; pointer, wheel, hover, layout, drawing, and viewport persistence never cross the Blazor circuit.

The server-owned UI catalog combines selected SPT global-locale values for shared concepts, QuestMap translations for custom controls, and per-key English fallback. Language-specific topology instances share one structural version so changing locale does not invalidate pan/zoom state.

## Server data composition

`QuestMapDataService` is the small public facade used by Razor. Its collaborators keep the two main data lifetimes explicit:

- `QuestTopologyBuilder` caches localized, profile-independent topology and delegates quest-template normalization to `QuestTemplateMapper`;
- `QuestZoneMapCatalog` loads the shipped internal-map-to-trigger catalog once; the server resolves every objective's trigger IDs to map IDs and computes each native-`Any` quest's authoritative actual-map union before either web or native-client transport;
- `QuestProfileStateBuilder` creates the uncached selected-profile overlay;
- `QuestGraphRules` owns topology/frontier/requirement algorithms;
- `QuestProfileRules` owns blocker, display-state, and objective-progress rules;
- `TraderAvailabilityEvaluator` encapsulates the selected profile's trader unlock rules, including direct completion checks for Introduction, Easy Money - Part 1 [PVE ZONE], and Knock-Knock;
- `QuestMapLocalizationService` resolves installed SPT locales and composes the bootstrap catalog.

QuestMap-owned translations live as embedded JSON resources under `Localization/Locales`. `QuestMapUiCatalog` contains only the composition policy: English fallback, selected SPT global-locale values, QuestMap overrides, and invariant product names. The English catalog contains only keys consumed by Razor or the Canvas renderer.

Static quest summaries are separate from SPT's authoritative locale text. The bundled `Localization/Summaries/en.json` catalog is embedded in the DLL. `QuestSummaryCatalog` loads every top-level JSON catalog from the server mod's `summaries` directory; `<name>.json` is English and `<name>.<language>.json` selects another SPT language. Files are merged in deterministic filename order, later values win per quest and language, and all custom values take precedence over the embedded catalog. Lookup prefers the requested custom language, then custom English, then the last available custom language for that quest so a non-English-only modded summary remains visible in every locale. Missing values do not create a Summary tab. Optional malformed/unreadable files are logged and skipped.

Zone-derived map data remains distinct from Tarkov's native quest location. Each objective carries its original zone IDs, server-resolved internal map IDs, and unresolved zone IDs. Native-`Any` quests additionally carry the localized, image-ready union as `ActualMaps` plus `ActualMapsComplete`. Non-spatial objectives do not invalidate a spatial union, while any unmapped spatial trigger keeps the visual fallback at `Any`. Factory day/night and Ground Zero low/high share the one canonical internal map ID supplied by `Data/triggerIds.json`. The shared client transport retains both task-level and quest-level fields. Native map sorting continues to use Tarkov's location; table membership and per-row objective visibility use task `MapIds`, and reusable native location visuals render the server union as compact text plus equal, center-cropped slices.

QuestMap-owned presentation assets live under `Presentation/Assets` as embedded assembly resources. The Razor page injects the stylesheet through `HeadContent` and imports the Canvas module from an assembly-generated JavaScript data URL. SPT 4.0.13 only maps a mod's physical `wwwroot` directory; embedding both assets keeps the deployment DLL-only, avoids an extra controller/static route, and avoids SPT's legacy-mod rejection of deployed `.js` files. Renderer colors are semantic CSS custom properties read through `getComputedStyle`; JavaScript contains no color literals.

## Security

- Reuse SPT's web authentication.
- Require the same or stronger permission as existing profile-management pages.
- Do not expose account secrets, inventory, messages, friends, or raw save files.
- Validate profile IDs against profiles loaded by the server.
- No write operations in v1.

## Asset strategy

Inspect 4.0.13 source/routes and quest/trader data to determine URLs already used by the game client.

Preferred order:

1. direct existing server asset URL;
2. normalized URL returned in topology DTO;
3. read-only proxy route only if direct use is impossible.

Do not copy image files into the mod package.

## Caching

Cache normalized topology per selected installed locale, using a locale-independent structural fingerprint for viewport persistence. Profile state responses should remain small and uncached or short-lived.

## Error handling

The UI should remain usable when:

- one image is missing;
- one quest condition type is unsupported;
- a profile disappears after deletion;
- a modded quest references a missing predecessor;
- a locale key is missing;
- objective progress cannot be computed.

Show warnings in diagnostics/logs without failing the entire graph.

## Blazor implementation

SPT 4.0.13 discovers QuestMap's mod-owned `/questmap` Razor page natively. Semantic page UI and testable state live in Blazor/C#, while the performance-critical Canvas renderer remains a browser-side `.mjs` module. See `docs/blazor-migration.md` for the source evidence, implemented boundary, performance rationale, and verification record.

Comparison mode keeps that boundary intact. Blazor loads two `ProfileStateDto` overlays through the same read-only service, derives symmetric multi-category comparison records and union-filter state in C#, then sends both overlays plus compact comparison records to the existing Canvas island. The renderer retains one topology, one layout, one edge layer and one viewport. Matching nodes reuse the ordinary card renderer at reduced emphasis; changed nodes draw explicit reason badges and A/B transition rows.

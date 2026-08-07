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
- `QuestProfileStateBuilder` creates the uncached selected-profile overlay;
- `QuestGraphRules` owns topology/frontier/requirement algorithms;
- `QuestProfileRules` owns blocker, display-state, and objective-progress rules;
- `TraderAvailabilityEvaluator` encapsulates the selected profile's trader unlock rules, including direct completion checks for Introduction, Easy Money - Part 1 [PVE ZONE], and Knock-Knock;
- `QuestMapLocalizationService` resolves installed SPT locales and composes the bootstrap catalog.

QuestMap-owned translations live as embedded JSON resources under `Localization/Locales`. `QuestMapUiCatalog` contains only the composition policy: English fallback, selected SPT global-locale values, QuestMap overrides, and invariant product names. The English catalog contains only keys consumed by Razor or the Canvas renderer.

Static quest summaries are separate from SPT's authoritative locale text. The bundled `Localization/Summaries/en.json` catalog is embedded in the DLL. `QuestSummaryCatalog` overlays an optional trader-specific catalog from `SPT/user/mods/SPT-QuestMap/Summaries`: `<traderId>.<language>.json` takes precedence, with `<traderId>.json` as the default or `<traderId>.en.json` only when the default file is absent. Lookup remains per quest, and missing values do not create a Summary tab. Optional malformed/unreadable files are logged and skipped.

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

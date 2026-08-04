# Blazor migration

## Implemented outcome

The migration described below was completed on 2026-08-04. `/questmap` is now a mod-owned Interactive Server Razor page discovered by SPT's existing Blazor router. The former MVC HTML action and monolithic `questmap.html` application were removed.

The implemented boundary matches the recommendation:

- Razor components own the header, profile and language selectors, filters, in-progress drawer, legend, selected-quest details, localization, loading/error state, and explicit refresh flow.
- `QuestMapPageState` owns testable C# filter, focus, selection, visibility, persistence, and drawer semantics.
- `QuestRichTextSanitizer` provides allowlisted C# rendering for quest descriptions and reward text, including paragraphs and supported Tarkov tags.
- The embedded `QuestMapRenderer.mjs` remains a browser-side Canvas island for layout, drawing, image loading, spatial indexes, hit testing, hover, pan, zoom, and animation-frame scheduling.
- Blazor passes its already-loaded topology and profile snapshot to the Canvas module during initialization and explicit data changes. Compact JS interop records carry ordinary filter/selection updates and quest selection events.
- C# owns serialization and v1→v2 migration for browser preferences stored under `sptQuestMap.ui.v2`; the Razor component performs only raw `localStorage` calls. The renderer owns the separately scoped `sptQuestMap.viewport.v2` record because viewport updates never cross the circuit.

Initial controller-free verification passed with 0 build warnings/errors and 81/81 tests. The installed SPT 4.0.13 host served the compiled Razor route over HTTPS during the initial cutover. Live interaction verified drawer-to-graph selection synchronization and reload persistence; the browser console contained no warnings or errors. The later asset consolidation embedded both CSS and the dynamically imported `.mjs` source into the DLL, so no QuestMap web asset file is deployed.

## Recommendation

QuestMap uses an SPT-hosted Blazor page while keeping the graph engine as a browser-side Canvas module.

This hybrid is the best long-term boundary:

- Blazor/C# owns the page route, controls, profile selection, localization, loading/error state, filters, selected-quest details, the in-progress drawer, and testable UI state transitions.
- Browser JavaScript owns layout coordinates, spatial indexes, Canvas drawing, image caching, hit testing, hover, pan, zoom, and animation-frame scheduling.
- Existing C# server data normalization remains the source of truth and should not be rewritten.

A fully server-interactive Canvas implementation is not recommended. SPT 4.0.13 enables Interactive Server Blazor, so frequent C#/JavaScript calls travel over the Blazor circuit. Pointer movement, wheel input, panning, and drawing must stay entirely in the browser to preserve the accepted renderer performance.

The implementation confirmed that this was a moderate refactor rather than a mechanical conversion. Reimplementing the renderer itself in C#/Razor remains intentionally out of scope because it would carry a high performance-regression risk without producing a clear maintenance benefit.

## Source-backed SPT 4.0.13 support

The supplied 4.0.13 source confirms native support for mod-owned Razor components:

- `SPTWeb.InitializeSptBlazor` selects mods implementing `IModWebMetadata`, registers every validated mod assembly as an MVC application part, registers MudBlazor, and calls `AddRazorComponents().AddInteractiveServerComponents()`.
- `SPTWeb.UseSptBlazor` maps controllers, calls `MapRazorComponents<App>().AddInteractiveServerRenderMode()`, and adds every web-mod assembly through `AddAdditionalAssemblies`.
- `Routes.razor` supplies those assemblies to the Blazor `Router` through `AdditionalAssemblies`.
- A mod can therefore expose `@page "/questmap"` from its own assembly without owning another web host.
- MudBlazor 8.13.0 is present and its services are already registered, although QuestMap does not need to adopt MudBlazor for the first migration.
- If present, a physical mod `wwwroot` directory is served at `/SPTQuestMap/`; SPT does not establish mod Razor Class Library `_content/...` discovery.

Exact reference files:

- `reference/spt-4.0.13-sources/Libraries/SPTarkov.Server.Web/SPTWeb.cs`
- `reference/spt-4.0.13-sources/Libraries/SPTarkov.Server.Web/Components/App.razor`
- `reference/spt-4.0.13-sources/Libraries/SPTarkov.Server.Web/Components/Routes.razor`
- `reference/spt-4.0.13-sources/Libraries/SPTarkov.Server.Web/IModBlazorMetadata.cs`
- `reference/spt-4.0.13-sources/Libraries/SPTarkov.Server.Web/SPTarkov.Server.Web.csproj`

This establishes the 4.0.13 integration path. It does not guarantee that the exact marker, target framework, MudBlazor version, or hosting details remain unchanged in a later SPT release; those should still be re-audited when QuestMap changes its supported SPT version.

## Pre-migration code inventory

The implementation had four distinct layers when this migration was planned:

| Area | Current size | Migration outcome |
| --- | ---: | --- |
| SPT data normalization, topology, profile overlay, DTOs, localization catalog | `QuestMapDataService.cs`, 1,337 lines / 83.6 KB | Preserve behavior, then split by responsibility |
| MVC route and read APIs | `QuestMapController.cs`, 85 lines | Removed after Blazor became the sole data owner |
| Browser page and application | `questmap.html`, 674 lines / 72.3 KB | Split between Razor components, C# state classes, and a Canvas module |
| Styling | `questmap.css`, 200 lines / 20.2 KB | Reuse approximately 80–90%; adjust selectors where Razor changes markup |

Physical line counts understate the browser complexity because many functions are deliberately dense. Responsibility is a more useful measure than raw lines.

### Reusable without conceptual changes

- `QuestMapDataService`, including SPT profile/quest/event/trader interpretation.
- All topology, profile-state, objective, blocker, exclusion, reward, trader, location, and localization DTO records.
- The topology-per-language cache and its structural version.
- The sanitized profile selection rules.
- `QuestMapDataService` methods and DTOs, consumed directly by the Razor page without a duplicate HTTP projection.
- The mod metadata marker. It already enrolls the assembly in both MVC and Blazor discovery.
- The current unit tests for SPT semantics and localization.
- Existing SPT asset URLs and graceful image fallbacks.
- Most CSS and all visual design decisions.

### Good candidates to migrate into Razor components and C# classes

- Profile picker and its keyboard/open/close state.
- Language selector and localized label lookup.
- Refresh, future, finished, level, search, and trader controls.
- Loading and error presentation.
- In-progress quests grouped by trader.
- Selected quest detail sections, objective/reward formatting, gates, exclusions, prerequisites, and direct successors.
- Filter, focus, and selection state transitions that can be represented as pure C# sets/records and unit tested.
- Display formatting such as status, location, faction, season, numbers, and available-after values.
- Typed localization key access, while retaining the existing server catalog and English fallback behavior.

### Code that should remain browser-side

- `requestAnimationFrame` scheduling and the fast-render interval.
- Canvas drawing, transforms, card/edge geometry, text wrapping, banner crossfades, route strips, and completion/terminal markers.
- Deterministic layered layout and compacted stable-position layout, unless a later benchmark justifies moving the pure calculation to a shared C# topology cache.
- Spatial grids for viewport culling and hit testing.
- Pointer capture, hover hit testing, drag/pan, wheel zoom, and resize observation.
- Browser image loading and image/fade-layer caches.
- Viewport persistence and high-frequency pan/zoom state.

Approximately 45–60% of the present browser application is part of that hot renderer path and should remain JavaScript. Roughly 25–35% is semantic UI/state logic that is well suited to C#/Razor. The remainder is initialization, persistence, and interop glue that will be rewritten around the new boundary. Across the entire repository, the expensive SPT interpretation logic and most presentation behavior are reusable; this is not a ground-up rebuild.

## Implemented target structure

```text
src/SPTQuestMap/
  Components/
    Pages/QuestMap.razor
    Layout/QuestMapLayout.razor
    QuestMapHeader.razor
    QuestMapFilters.razor
    InProgressDrawer.razor
    QuestDetails.razor
    QuestLegend.razor
    RichTextBlock.razor
  Presentation/
    QuestMapPageState.cs
    QuestMapLocalizer.cs
    QuestMapSettingsStorage.cs
    QuestRichTextSanitizer.cs
  Services/
    QuestMapDataService.cs
    QuestTopologyBuilder.cs
    QuestTemplateMapper.cs
    QuestProfileStateBuilder.cs
    TraderAvailabilityEvaluator.cs
    QuestGraphRules.cs
    QuestProfileRules.cs
    QuestMapLocalizationService.cs
    QuestMapProfilePolicy.cs
    QuestMapQuestIds.cs
  Models/
    QuestMapDtos.cs
  Localization/
    QuestMapUiCatalog.cs
    Locales/*.json
  Presentation/
    Assets/
      QuestMap.css
      QuestMapRenderer.mjs
    QuestMapEmbeddedAssets.cs
```

`QuestMap.razor` uses `@page "/questmap"`. The old MVC page and read controller have been removed; the Razor page calls `QuestMapDataService` once and supplies the resulting immutable snapshot to the renderer.

The post-migration server cleanup reduced `QuestMapDataService` to an 80-line facade. Profile-independent topology caching, template mapping, profile overlay derivation, graph/profile rules, trader availability, DTOs, and locale resources now have separate files. QuestMap translations are embedded JSON rather than parallel hard-coded arrays in the catalog class.

QuestMap uses its own minimal full-screen layout. It does not adopt `BaseMudBlazorLayout`, avoiding unnecessary Mud styling/providers and an external Google Fonts dependency for the bespoke graph.

## Component and renderer contract

The graph should be treated as a browser-rendered island inside a stable Razor-owned `<canvas>` container.

The component initializes one JavaScript module in `OnAfterRenderAsync(firstRender)`. The module should expose a small command surface such as:

```text
initialize(canvas, topology/profile/view snapshot)
refresh(topology/profile/view snapshot)
setVisibleQuestIds(ids)
setSelection(selected ID, prerequisite IDs, successor IDs, highlighted edge IDs)
setFocus(focused ID)
centerQuest(quest ID)
fitVisible()
dispose()
```

The browser should call .NET only for low-frequency semantic events:

```text
questSelected(quest ID)
questDoubleClicked(quest ID)
viewportPersistRequested(view state)   // debounced, or persisted wholly in JS
rendererFailed(message)
```

Do not send pointer-move, hover, wheel, frame, or draw-primitive events through Blazor. Hover highlighting can remain a renderer-only transient overlay. Selection should be reported once, allowing Razor to update the detail pane and in-progress drawer.

The page already requires the topology and profile overlay to render controls and details. Passing that same immutable snapshot through `IJSRuntime` during initialization and explicit data changes avoids a second service call, a second serialization pass, and anonymously addressable JSON routes. High-frequency rendering state still never crosses interop.

## State ownership

Keep the following distinctions explicit:

| State | Owner | Persistence |
| --- | --- | --- |
| Cached localized topology and SPT interpretation | Singleton `QuestMapDataService` | Server process |
| Selected profile, language, filters, selected/focused quest | Blazor page state | Batched browser storage plus current circuit |
| Pan, zoom, dragging, hover, image cache, spatial indexes | Canvas module | Browser memory; pan/zoom persisted locally |
| Quest detail view model | Blazor page/component | Rebuilt from selected ID and current immutable snapshots |

Do not put per-browser UI state into the singleton `QuestMapDataService`. Interactive Server creates separate circuits and component state per browser tab. The page can own a `QuestMapPageState` instance directly, or use a scoped service only if sharing across unrelated components becomes useful.

The existing `localStorage` record should remain the durable source for user preferences. Blazor circuit state alone does not survive a server restart or a full reload. Load and save browser settings in one batched JS interop call; never persist every pan event across the server circuit.

## Rendering performance assessment

### Safe improvements

- Razor-render the header, filter controls, drawer, legend, and one selected detail pane. These are small DOM trees updated at human interaction frequency.
- Move filter/selection semantics to pure C# classes and test them, then send only ID sets to the Canvas module.
- Retain the current Canvas overview threshold, spatial buckets, lazy images, transform-only panning, and edge hiding during active pan/zoom.
- Prevent the graph component from rerendering or being replaced when unrelated Razor state changes. Use a stable component instance and element reference.
- Keep topology and profile snapshots immutable for one explicit-refresh cycle.

### Regressions to avoid

- One Razor component per graph node and one SVG/DOM element per edge. Hundreds of component instances and render-tree diffs would reintroduce the performance problem the Canvas renderer solved.
- C# drawing through one JS interop call per Canvas primitive. Interactive Server interop is asynchronous network traffic over SignalR.
- Reporting pointer movement or animation frames to .NET.
- Passing the complete topology for ordinary filter, selection, pan, zoom, or render updates. Full snapshots are limited to initialization and explicit data changes.
- Letting a toolbar/detail update replace the `<canvas>` element and lose its rendering context.
- Running graph layout from `OnAfterRenderAsync` after every component render.

### New Blazor-specific operational costs

- Each open tab has an Interactive Server circuit and server-held component state.
- A lost Blazor connection temporarily disables Razor controls even though the last Canvas frame remains visible. Add the standard reconnect UI and ensure localStorage restoration makes a reload harmless.
- Razor, CSS, and Canvas-module edits change the mod DLL and therefore require an SPT server restart. This trades static-only style deployment for one self-contained, controller-free artifact.
- Pre-rendered components cannot use JS interop until the first interactive render. Renderer initialization belongs in `OnAfterRenderAsync`, with cancellation/disposal handling for a disconnected circuit.

Microsoft's Interactive Server documentation confirms that UI events and JS calls travel through a SignalR connection and that each browser screen owns a circuit. Its JS interop guidance also warns against high-volume calls and large serialized payloads. These constraints support retaining the renderer hot path in JavaScript:

- <https://learn.microsoft.com/aspnet/core/blazor/hosting-models>
- <https://learn.microsoft.com/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet>
- <https://learn.microsoft.com/aspnet/core/blazor/performance/javascript-interoperability>
- <https://learn.microsoft.com/aspnet/core/blazor/host-and-deploy/server/memory-management>

## Static asset and build constraints

### JavaScript filename restriction

SPT 4.0.13 `ModValidator.ValidMod` recursively rejects any server-mod directory containing `*.js` or `*.ts`, classifying it as a legacy pre-4.0 mod. Razor JavaScript collocation (`Component.razor.js`) therefore cannot be deployed normally.

The implemented application embeds the `.mjs` source in the mod assembly and imports an assembly-generated `data:text/javascript;base64,...` URL. No JavaScript file is deployed, and the build/deploy audit continues to reject deployed `.js` and `.ts` files.

### Razor compilation

Change the mod project from `Microsoft.NET.Sdk` to `Microsoft.NET.Sdk.Razor` for the migration branch while preserving:

- `TargetFramework` `net9.0` for SPT 4.0.13;
- output type `Library`;
- installed SPT references with `Private=false`;
- one top-level mod metadata implementation;
- the embedded localization and presentation resources used by the compiled Razor page.

SPT explicitly maps only a deployed physical mod `wwwroot`; it does not establish Razor Class Library `_content/...` discovery for the dynamically loaded assembly. QuestMap therefore embeds its CSS and Canvas module directly. `QuestMap.razor` emits the CSS in `HeadContent` and imports the module through a data URL, leaving `wwwroot` empty and removing all QuestMap static-file routes. Avoid `.razor.css` isolation unless SPT later proves generated static-web-asset discovery for mod assemblies.

If MudBlazor components are used, add an explicit non-copying compile reference to the installed `MudBlazor.dll`. SPT already registers Mud services and loads the runtime assembly. Selective use is preferable to redesigning the accepted visual system around Mud controls during the port.

## Rich text and localization

The current browser sanitizer supports paragraphs/newlines, safe common HTML, and Tarkov/Unity `color`, `size`, and `align` tags while preserving unknown placeholders as visible text.

Blazor's `MarkupString` is not a sanitizer. Port the existing behavior into a dedicated, unit-tested `QuestRichTextSanitizer` before rendering sanitized output as markup. Do not place raw locale/mod quest HTML into `MarkupString`. A small maintained HTML parser/sanitizer dependency is safer than regex-only parsing, but it must be packaged and versioned deliberately; an allowlisted C# implementation is also acceptable if the current fixtures are retained as parity tests.

Keep `QuestMapUiCatalog` as the source of localized QuestMap-owned strings. Add a small typed/localizer wrapper so Razor components use keys consistently. Changing language must refresh localized topology and browser locale formatting without replacing the graph viewport or selection.

## Migration stages (completed)

### Stage 0 — compile and host spike (0.5–1 day)

- Create a migration branch from the 1.0 commit.
- Switch to the Razor SDK.
- Add a minimal mod-owned `@page "/questmap-blazor-spike"` component.
- Confirm route discovery, Interactive Server behavior, DI injection of `QuestMapDataService`, embedded styling/module initialization, and visible reconnect behavior.
- Verify an `.mjs` module import under the installed 4.0.13 server.
- Do not replace `/questmap` yet.

Exit: source-backed assumptions are proven against the installed server without disturbing 1.0.

### Stage 1 — Blazor shell beside the current app (1–2 days)

- Build the custom layout, header, profile picker, language selector, loading/error handling, and persistent page state.
- Call `QuestMapDataService` directly for server-owned models and pass immutable snapshots to the renderer.
- Keep the existing page available for side-by-side parity.

### Stage 2 — extract the Canvas renderer (2–4 days)

- Move only hot renderer responsibilities into the `.mjs` module.
- Define the small interop contract above.
- Preserve current layout, spatial indexing, overview rendering, image behavior, and viewport storage.
- Add JavaScript unit/smoke coverage where practical and retain browser performance metrics.

### Stage 3 — migrate semantic UI (2–4 days)

- Move filters, selection/focus rules, drawer, detail pane, rewards/objectives, relation navigation, formatting, localization, and rich-text sanitation to C#/Razor.
- Add component/presentation tests for state transitions and sanitized rich text.
- Ensure filters never affect the in-progress drawer and focus mode retains its accepted overrides.

### Stage 4 — parity, performance, and route cutover (2–3 days)

- Run the full acceptance checklist against both pages.
- Compare node/edge totals, default frontier, filters, selection/focus, viewport restoration, localization, and detail content.
- Compare layout/render/overlay metrics and visible interaction behavior on the complete graph.
- Remove the MVC HTML action, assign `@page "/questmap"`, remove the legacy inline HTML after rollback confidence is sufficient, and update build/deploy checks.

## Acceptance gates for the port

- Existing 71 server/state tests remain green before UI-specific additions.
- Existing semantic DTOs remain the shared Blazor/renderer contract without duplicate MVC endpoints.
- Full graph metrics do not regress materially from the accepted Canvas implementation.
- Pan, zoom, wheel, hover, and animation frames produce no .NET callbacks or per-primitive JS interop.
- Profile/language refresh remains explicit and preserves pan, zoom, selection, focus, and applicable settings.
- Browser reload and server restart restore local settings safely.
- No deployed `.js` or `.ts` file exists; the renderer asset delivery is verified on SPT 4.0.13.
- `/questmap` has exactly one endpoint after cutover.
- Quest descriptions/rewards retain the current safe HTML and Tarkov-tag rendering.
- Static assets still come from SPT routes and no artwork is copied.

## Final assessment

Blazor is a good maintainability direction for QuestMap because SPT already owns the component host, router, DI container, server runtime, and optional MudBlazor stack. It will replace a large monolithic inline browser application with ordinary C# components and testable presentation classes.

The performance win already achieved by the Canvas renderer should be treated as an architectural boundary, not temporary implementation detail. The recommended port is therefore a Blazor application with a Canvas renderer island, not a pure-C# rendering rewrite. This delivers most of the maintainability benefit with controlled effort and keeps the graph's accepted behavior intact.

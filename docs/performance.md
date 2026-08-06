# Performance requirements

The previous static experiment demonstrated that hundreds of DOM cards plus hundreds of SVG edges can become unpleasantly slow. Treat rendering performance as a first-class design concern.

## Required interaction behavior

- Panning and zooming must be transform-only during pointer movement.
- Do not rerun graph layout during pan/zoom.
- Do not rebuild status panels or DOM trees every animation frame.
- Selection/highlight opacity must remain correct while panning.
- Browser resize must preserve the current viewport and selected node.
- Profile refresh should update node/edge styles and details without recomputing static topology unless the topology version changed.

## Rendering strategy

The implementation may use:

- Canvas;
- WebGL;
- a proven high-performance graph library;
- a hybrid of Canvas/WebGL edges and lightweight HTML overlays;
- a carefully optimized SVG solution only if profiling proves it remains responsive.

Do not mandate one technology before inspecting the existing page and available SPT web toolchain.

## Avoid

- one expensive event listener per edge;
- scanning every edge on pointer move;
- large CSS filters, shadows, or backdrop effects on every node;
- full-resolution image decoding for nodes too small to read;
- recomputing paths while merely panning;
- relayout on every filter toggle when simple visibility changes suffice;
- DOM-per-edge if it is the bottleneck.

## Overview rendering

At low zoom, full text/image cards may be replaced by simplified state-colored blocks. Selection, completion state, and highlighted prerequisite paths must remain visible in overview mode.

## Layout

- Prefer left-to-right prerequisite progression.
- Avoid backward edges where possible.
- Keep effective-level boundaries legible.
- More heavily constrained/multi-prerequisite quests may be placed farther right.
- Layout should be deterministic for a stable topology.
- Focused-chain layout may compact around the selected quest while preserving its screen anchor and current zoom.

## Measurement

Record at least:

- node and edge counts;
- initial layout duration;
- initial render duration;
- profile overlay refresh duration;
- pan/zoom responsiveness on the user's browser;
- any overview-mode threshold or virtualization behavior.

A hard FPS number is not required before real hardware testing, but visible stutter, delayed drag response, or multi-second filter changes are release blockers.

## Implemented Milestone 3 renderer

QuestMap's Blazor page treats the graph as a browser-rendered island. `questmap-renderer.mjs` uses one Canvas for both nodes and edges; Razor components never render one component per graph node or edge. Static topology receives a deterministic left-to-right layered layout once per topology fingerprint. Node rectangles and edge bounds are then placed in fixed-size spatial buckets; viewport rendering and pointer hit-testing query only intersecting buckets instead of scanning the complete graph during pointer movement.

Pointer movement, hover, pan, zoom, image decoding, hit testing, and animation frames stay entirely inside the browser module. Blazor receives only low-frequency quest click/double-click and renderer-failure callbacks. The already-loaded topology/profile snapshot crosses JS interop once at initialization and again only on explicit refresh, profile change, or language change; ordinary view updates send compact ID-only records.

- Pan and zoom update only the viewport transform and schedule one animation-frame render. They do not run layout, rebuild the details panel, or scan all edges.
- Profile refresh replaces the state/trader/objective overlay while retaining layout when the topology fingerprint is unchanged.
- Filters alter membership against stable coordinates. Only the explicit focused-chain action calculates a compact alternate layout.
- Focus and clear-focus preserve zoom and re-anchor the selected quest to its prior screen position.
- Below `0.48` zoom, nodes render as state-colored overview blocks without text or image decoding. Highlighted paths and selection outlines remain visible.
- Quest and trader images are requested lazily from their SPT asset URLs only when a node is rendered above the overview threshold.
- Resize changes the Canvas backing size and redraws the same viewport; it does not call fit or reset pan/zoom.
- Daily/Weekly nodes remain a small profile-owned overlay. The renderer composes their horizontal band above the active static layout without adding them to the cached dependency topology; a one-second browser timer redraws only the Canvas countdown and never polls the server.
- Comparison adds a second state lookup and a precomputed quest-to-category map while retaining the same topology, spatial indexes and edge geometry. Each visible node performs constant-time comparison and A/B lookups; pan, zoom and hover continue to avoid full-graph scans or Blazor renders. Category filtering recalculates membership only when a filter changes.

The bottom-right runtime metric bar records visible/applicable node counts, visible edge count, layout duration, profile-overlay request/application duration, last render duration, and whether overview mode is active. Final user-hardware measurements belong to Milestone 4.

## Native production renderer (in-game Milestone 5)

The reusable native `QuestGraphView` receives any `IQuestGraphProjection`; trader ownership is now only one projection builder and screen-mount adapter. Static topology/layout, live overlay, selection, and viewport state remain separate inputs.

- Node cards are acquired from `QuestGraphNodePool`. A pure bucketed `QuestGraphSpatialIndex` identifies the padded viewport set; panning recycles nodes leaving that set and binds only nodes entering it. Static labels/geometry bind on acquisition, status/progress updates touch the live overlay, and selection updates only the highlight layer.
- Deterministic edge ports and cubic routes are calculated once by `QuestEdgeRoutePlanner`. `QuestGraphEdgeLayer` partitions routes into fixed 128-edge `MaskableGraphic` batches, avoiding both per-edge GameObjects and Canvas vertex-limit pressure. The viewport `RectMask2D` clips batches. Pan/zoom moves the common content transform and does not dirty edge meshes; only topology/geometry or selection-style changes rebuild them.
- Drag and cursor-centered wheel zoom remain transform-only. Explicit plus/minus, Fit, and Center Selected controls share the same transform path. Resize preserves the transform and merely refreshes node culling.
- Viewport and selected quest are stored in Unity `PlayerPrefs` under a stable hash scoped by topology version, profile ID, and view type (`trader:<id>`). In-memory topology replacement preserves the current transform, then saves it into the new scope; stale layouts cannot restore into a different topology version.
- `QUESTMAP_M02_TOPOLOGY` and `QUESTMAP_M02_OVERLAY` retain topology, overlay, and layout timings. `QUESTMAP_M05_RENDER` adds first-render/node/edge/active-pool counts. Debug mode adds edge-mesh, overlay-refresh, and disposal high-water metrics without per-frame logging.

Pure regressions cover deterministic full layout/routes, shared server/client rules, selection semantics, overlay-without-layout replacement, and spatial queries representing 1920×1080, 2560×1440, and 3440×1440 viewports. Final responsiveness and lifecycle evidence remains an in-game manual gate.

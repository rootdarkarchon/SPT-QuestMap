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

The bottom-right runtime metric bar records visible/applicable node counts, visible edge count, layout duration, profile-overlay request/application duration, last render duration, and whether overview mode is active. Final user-hardware measurements belong to Milestone 4.

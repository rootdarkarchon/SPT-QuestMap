# Milestone 5 — Production Renderer and Shared Core

## Objective

Turn the trader graph into a reusable, performant graph component and extract stable runtime-neutral logic shared by server and client.

## Prerequisite

Milestone 4 must prove the trader graph lifecycle and native action behavior.

## Shared core

Create or complete:

```text
src/SPTQuestMap.Core/
```

Move only verified pure logic:

- normalized topology contracts;
- normalized profile overlay contracts;
- graph edge contracts;
- traversal;
- filtering;
- known-plus-frontier visibility;
- route calculations;
- deterministic layout;
- shared display-state classifications.

Adapt server and client enums at their boundaries.

The core must not reference ASP.NET, Blazor, JavaScript, Unity, BepInEx, server assemblies, or EFT client assemblies.

Existing `/questmap` behavior and tests must remain unchanged.

## Production node renderer

Use pooled node views.

Separate updates for:

- static node content;
- status/progress overlay;
- selection/highlight overlay;
- visibility;
- viewport culling.

Do not recreate every node on pan, zoom, selection, or status refresh.

## Production edge renderer

Do not use one GameObject, `Image`, or `LineRenderer` per edge.

Implement a batched renderer using an appropriate verified Unity UI primitive, such as:

```text
Graphic
MaskableGraphic
VertexHelper
```

Support:

- success/failure/started/any-outcome edge styles;
- selected prerequisite highlighting;
- direct successor highlighting;
- clipping or viewport culling;
- mesh rebuild only when geometry or relevant style changes.

Panning should normally move a parent transform rather than regenerate geometry.

## Viewport behavior

Implement:

- drag pan;
- cursor-centered wheel zoom;
- zoom controls;
- fit visible;
- center selected;
- resize handling;
- persisted viewport;
- persisted selection;
- persistence scoped by topology version, profile, and view type.

Use an appropriate client-side storage mechanism.

## Performance instrumentation

Debug logs or diagnostics should measure:

- topology build time;
- overlay build time;
- layout time;
- first render time;
- node/edge counts;
- active pooled node count;
- edge mesh rebuild time;
- overlay refresh time.

## Tests

Verify:

- server and client consume shared rules consistently;
- existing server tests pass;
- full graph layout remains deterministic;
- ordinary status changes do not rebuild layout;
- rapid pan/zoom remains responsive;
- repeated filter changes do not leak views;
- 1080p, 1440p, and available ultrawide layouts.

## Documentation

Update architecture, performance findings, and shared-core boundaries in repository docs.

Update `docs/development-status.md`.

## Acceptance gate

Milestone 5 is complete when:

- the graph renderer is reusable outside the trader screen;
- pooled nodes and batched edges are in place;
- viewport persistence works;
- shared pure logic is consumed where appropriate;
- browser behavior has not regressed;
- the full topology remains responsive.

When complete, proceed to `M06-global-tasks-screen-replacement.md`.

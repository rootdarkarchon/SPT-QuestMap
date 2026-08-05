# AGENTS.md — SPT-QuestMap

## Mission

Build **SPT-QuestMap**, a read-only, profile-aware quest dependency map for the currently installed **SPT 4.0.13** server.

The page must be served by the SPT server at:

```text
/questmap
```

Use the existing SPT ASP.NET/Kestrel host and its supported server-mod web integration. Do not launch a separate web server unless the provided 4.0.13 source proves that integration is impossible.

## Start here

1. Read this file.
2. Read `README.md` and `docs/development-status.md`.
3. Read the relevant documents in `docs/`.
4. Inspect `reference/existing-quest-graph/index.html` as a visual and interaction reference only.
5. Ask the user to place matching SPT 4.0.13 sources under `reference/spt-4.0.13-sources/` if they are not already present.
6. Inspect those sources before choosing exact APIs, interfaces, namespaces, routes, or deployment layout.

## Source-first rule

The installed server is the runtime target, but the user can provide the matching SPT 4.0.13 source tree.

- **Do not decompile installed SPT DLLs.**
- Do not infer public APIs from current 4.1 documentation when the 4.0.13 source can answer the question.
- Reading installed configuration, database JSON, profile JSON, logs, and directory layout is allowed.
- Referencing the installed server assemblies for compilation is acceptable if that is the normal 4.0.13 mod workflow, but use the supplied source tree to understand behavior.
- If the supplied source and installed binaries do not match, stop and report the mismatch rather than guessing.

## Product requirements

### Profile awareness

- List every valid profile loaded by the server.
- Let the user select a profile from the page.
- Persist the selected profile in browser storage.
- Expose only sanitized profile data to the browser. Never return an entire raw profile.
- The initial implementation is strictly read-only. Do not add profile mutation endpoints.

### Quest states

Render quests according to the selected profile, including at minimum:

- locked / unavailable
- gated behind PMC level
- gated behind trader loyalty or reputation
- gated behind an unavailable trader
- available to start
- waiting for `availableAfter`
- started / in progress
- available to finish
- completed successfully
- failed
- permanently failed or excluded
- restartable failure where applicable
- expired where applicable

Use the exact quest-status model from SPT 4.0.13.

### Dependency graph

- Use the current node graph as a UX reference, but reimplement as needed.
- No bridge aliases are needed; this is a graph.
- Show status-sensitive prerequisite edges, including success/failure requirements using color.
- Mutually exclusive quest branches must be discoverable and visually distinct.
- A completed quest that excludes another branch should make the exclusion reason apparent.
- Clicking a node highlights all recursive prerequisites and only its direct successors.
- A dedicated focus/filter action may compact the graph to the selected node, all recursive prerequisites, and direct successors.
- Resizing the browser must not reset pan or zoom.

### Future quest depth

Default view:

- show all profile-known quests: active, available, completed, failed, pending, or otherwise present in profile state;
- show the immediate next tier of locked future dependencies;
- do not recursively show the entire future graph.

Provide a toggle to show all future quests.

The exact frontier algorithm should be documented and unit-tested. Prefer using SPT's own availability checks where practical instead of maintaining a subtly different clone.

### Finished and failed visibility

Provide a toggle to hide:

- successfully completed quests;
- permanently failed or mutually excluded quests.

Do not hide active, hand-in-ready, available, pending, or restartable-failure quests with this toggle.

### Quest objectives

- Started quests must show objectives and profile progress.
- Show fulfilled objectives and numerical progress where SPT stores enough information to calculate it.
- Never invent progress that is not present in the profile/server state.
- Clicking any quest—locked or unlocked—must show its objective definitions.
- Preserve objective order.
- When objectives are interdependent, order them using their explicit index/dependency/visibility relationships rather than incidental JSON iteration order.
- Show objective status clearly for active quests.

### Faction and seasonal filtering

- USEC/Bear-specific quests are filtered automatically from the selected profile's faction. No faction toggle is needed.
- Seasonal quests must follow the server's actual event state and quest event configuration.
- Off-season Christmas/Halloween quests are hidden and excluded from totals.
- Entries classified as event season `None` are excluded completely, along with descendants that only exist behind those removed event quests, matching the prior QuestMap behavior.
- Do not duplicate date/event logic if SPT exposes authoritative helpers/services.

### Traders

- Display each quest's trader.
- Show whether that trader is actually unlocked for the selected profile.
- Show effective inherited loyalty/reputation requirements, e.g. `Prapor LL2` or `Fence Rep <= -1`.
- Quest availability must respect the profile's real trader unlock, loyalty, and standing state.

### Assets

Do not bundle copied quest or trader artwork.

- Discover how SPT 4.0.13 already serves quest icons and trader portraits to clients.
- Use those existing server asset URLs or a thin URL-normalizing endpoint if required.
- Reuse quest image paths already present in quest templates when possible.
- Provide graceful text/initial fallbacks for missing assets.

### Refresh and persistence

- No automatic refresh is needed.
- Add a visible refresh button that reloads server/profile state without losing the user's view unnecessarily.
- Persist relevant UI settings in browser storage, including:
  - selected profile;
  - selected/highlighted quest;
  - focused/filtered quest chain;
  - future-depth mode;
  - hide-finished mode;
  - trader/search filters where applicable;
  - pan and zoom, scoped so stale layouts do not corrupt a new graph version.

### Performance

Performance is a product requirement, not a cleanup task.

- Test with the complete quest graph and hundreds of edges.
- Avoid rebuilding the graph during pan/zoom.
- Avoid O(all edges) work on every pointer movement.
- Avoid expensive full-card DOM/SVG repainting at overview zoom levels.
- Choose Canvas, WebGL, a proven graph renderer, or a carefully profiled hybrid approach as appropriate.
- Preserve selection/highlight state while panning and in overview mode.
- Keep layout calculation separate from render-state overlays so profile refreshes do not unnecessarily recompute topology.
- Document the chosen rendering strategy and its measured behavior.

## Web/API guidance

The exact structure is intentionally not prescribed. A sensible result will usually contain:

- a static/cached graph topology and quest metadata layer;
- a small profile list endpoint;
- a selected-profile quest-state endpoint;
- a page or static app at `/questmap`;
- server asset URLs for images.

Use the existing SPT authentication/authorization facilities. Profile listing and profile quest state should not be exposed anonymously if the server web UI normally protects profile data.

Do not create write endpoints in the first version.

## Build and deployment

- Build against the installed SPT 4.0.13-compatible references and matching source.
- Deploy only after a successful build and test pass.
- Default deployment target is expected to be under the SPT server's mod directory, but confirm the exact 4.0.13 layout from source and the installed server.
- If the deployed DLL changed, restart the SPT server using the user-provided restart command.
- Treat that restart as part of the deployment task: run the configured command yourself instead of leaving the restart as a manual user step. If no command is configured, ask for it before deployment.
- Do not guess or hard-code the restart command.
- Static-only changes may be copied without restart if the 4.0.13 host serves them dynamically; verify this rather than assuming it.
- `scripts/deploy.ps1` is a starting hook and may be adapted after source inspection.

## Development discipline

- Maintain `docs/development-status.md` with the current milestone, completed work, blockers, and the next concrete step.
- Add every new `.csproj` to `src/SPTQuestMap/SPTQuestMap.slnx` in the same change.
- Keep commits/milestone changes reviewable.
- Prefer tests around graph/state logic over screenshot-only validation.
- Add fixture profiles only after sanitizing them.
- Log unsupported or ambiguous quest conditions rather than silently misclassifying them.
- Keep compatibility work for 4.1 out of scope unless it naturally costs almost nothing. The target is 4.0.13.

## Definition of done

The project is done when the acceptance checklist in `docs/acceptance.md` passes on the user's installed SPT 4.0.13 server, the page is reachable at `/questmap`, profile selection and refresh work, quest state/objective/exclusion data is accurate, and pan/zoom remains responsive on the full graph.

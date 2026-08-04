# Milestones

Keep these milestones reviewable. Do not turn each bullet into its own phase.

## Milestone 1 — source reconnaissance and skeleton

- Inspect matching SPT 4.0.13 source.
- Identify exact mod lifecycle/metadata interfaces.
- Identify web page, static asset, controller/API, authentication, profile, quest, event, trader, and asset integration points.
- Create a buildable mod skeleton.
- Serve a minimal authenticated page at `/questmap`.
- Document build and deployment output in `development-status.md`.

Exit condition: a minimal mod builds, deploys, and serves `/questmap` on the installed server.

## Milestone 2 — normalized data and profile overlay

- Build/cache quest topology from server tables/config/locales.
- List sanitized profiles.
- Load selected-profile quest/trader/faction state.
- Implement seasonal and `None` event filtering.
- Implement status, gates, mutual exclusion, and objective progress models.
- Add unit tests and sanitized fixtures.

Exit condition: API/DTO output accurately represents several representative profiles without a graph UI.

## Milestone 3 — graph UI and interactions

- Reimplement the reference graph using a performant renderer.
- Add profile selector, refresh, future-depth toggle, hide-finished toggle, selection, focus-chain action, details/objectives panel, legend, and asset loading.
- Preserve state in browser storage.
- Preserve pan/zoom on resize and refresh.

Exit condition: the complete graph is usable and profile-aware at `/questmap`.

## Milestone 4 — validation and deployment polish

- Test mutual exclusions, active quests, ready-to-finish quests, level/trader gates, faction quests, seasonal quests, and objective counters.
- Profile rendering and interaction performance.
- Finalize build/deploy/restart script.
- Add clear diagnostics for unsupported modded quest conditions.
- Complete `docs/acceptance.md` against the installed server.

Exit condition: repeatable build/deploy flow and acceptance checklist pass.

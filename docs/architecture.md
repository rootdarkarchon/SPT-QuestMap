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

## Likely server responsibilities

- integrate a mod page at `/questmap`;
- serve static JS/CSS through the existing host;
- expose a sanitized list of profiles;
- expose normalized graph metadata or enough raw data for the client to normalize;
- expose selected-profile quest/trader state;
- provide existing asset URLs;
- enforce existing SPT web authentication/authorization.

A single combined endpoint is acceptable for the first implementation, but avoid sending full profile JSON.

## Likely browser responsibilities

- graph layout and rendering;
- pan/zoom and selection;
- profile/status color overlay;
- filters and future-depth view;
- objective details panel;
- localStorage persistence;
- explicit refresh.

Server-side layout is also acceptable if it materially improves performance or determinism, but browser viewport state must remain independent.

## Suggested API shape

Names are examples only:

```text
GET /questmap/api/profiles
GET /questmap/api/bootstrap?language={code}
GET /questmap/api/topology?language={code}
GET /questmap/api/profiles/{profileId}/state
GET /questmap/api/runtime
```

Possible split:

- `profiles`: sanitized profile summaries;
- `bootstrap`: installed SPT languages, resolved selection, and browser UI catalog;
- `topology`: cacheable quest nodes, edges, locale text, and asset URLs;
- `state`: selected profile quest/trader/objective overlay;
- `runtime`: event state and topology version.

The implemented bootstrap endpoint keeps QuestMap-owned strings out of the static page. Its catalog combines selected SPT global-locale values for shared concepts, QuestMap translations for custom core controls, and per-key English fallback. Language-specific topology instances share one structural version so changing locale does not invalidate pan/zoom state.

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

# SPT-QuestMap Codex Handoff

This package is a project brief and starter workspace for building **SPT-QuestMap** against SPT **4.0.13**.

## Before starting Codex

1. Extract this archive into a clean working directory.
2. Put the matching SPT 4.0.13 source tree under:

   ```text
   reference/spt-4.0.13-sources/
   ```

3. Optionally place sanitized profile fixtures under:

   ```text
   reference/test-profiles/
   ```

4. Open the extracted directory as the Codex workspace.
5. Tell Codex to read `AGENTS.md` and `CODEX_HANDOFF.md` and begin Milestone 1.

## Important

The current static graph is included only as a reference:

```text
reference/existing-quest-graph/index.html
```

The production mod should obtain quest templates, locale data, event state, profile state, and image URLs from the installed SPT server rather than requiring copied JSON or artwork.

## User-provided values still needed

- path to the installed SPT server;
- matching 4.0.13 source tree;
- build/deployment details discovered from that source;
- server restart command;
- optionally one or more sanitized profiles representing useful quest states.

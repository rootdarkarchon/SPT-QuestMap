# SPT-QuestMap In-Game UI — Codex Handoff Bundle

This bundle splits the in-game QuestMap project into bounded Codex milestones so a new session does not need the complete project plan in context.

## How to use it

For the first Codex session, provide only:

1. this `README.md`;
2. `SHARED-PROJECT-CONTRACT.md`;
3. `VERIFIED-SOURCE-LEADS.md`;
4. `milestones/M00-installed-client-investigation.md`;
5. the repository itself.

For later sessions, provide only:

1. this `README.md`;
2. `SHARED-PROJECT-CONTRACT.md`;
3. the current milestone file;
4. `docs/development-status.md` from the repository;
5. `VERIFIED-SOURCE-LEADS.md` only when exact client-source context is needed.

Do **not** preload future milestone files. The current milestone's acceptance gate determines when the next file becomes relevant.

## Session starter

Use this prompt at the beginning of a Codex session:

```text
Work in the SPT-QuestMap repository. Read the repository AGENTS.md, README.md,
docs/development-status.md, the attached SHARED-PROJECT-CONTRACT.md, and the
attached current milestone file. Treat the current milestone as the active scope.
Inspect the working tree before editing and preserve unrelated changes.

Continue implementation rather than merely restating the plan. Complete the
milestone acceptance gate where the local environment permits it. Run relevant
builds/tests, update docs/development-status.md using the supplied status format,
and report exact blockers instead of guessing.
```

## Milestone order

| Milestone | File | Purpose |
|---|---|---|
| 0 | `M00-installed-client-investigation.md` | Verify exact client assemblies, quest data, UI hierarchy, and native action flow. |
| 1 | `M01-client-project-compatibility-harness.md` | Create a loadable, inert BepInEx client project with exact-version guards. |
| 2 | `M02-graph-model-client-data-adapter.md` | Build tested topology, overlay, filtering, and deterministic layout logic. |
| 3 | `M03-trader-graph-vertical-slice.md` | Replace the per-trader quest list with a graph while retaining native details. |
| 4 | `M04-native-actions-reactive-updates.md` | Prove native actions and event-driven graph refreshes. |
| 5 | `M05-production-renderer-shared-core.md` | Add pooling, batched edges, persistence, performance work, and shared core extraction. |
| 6 | `M06-global-tasks-screen-replacement.md` | Add In Progress, full Quest Map, Quest Items, and Notes tabs. |
| 7 | `M07-custom-detail-pane.md` | Add the QuestMap-owned detail pane while retaining native action controllers. |
| 8 | `M08-packaging-release-hardening.md` | Compatibility handling, packaging, docs, regression testing, and release readiness. |

## Milestone transition rule

A milestone is complete only when its acceptance gate is met and recorded in `docs/development-status.md`.

If manual in-game validation is required before proceeding, Codex should leave the code buildable, provide an exact test checklist, and mark the milestone `Partial — awaiting manual verification` rather than speculating.

## Bundle files

- `SHARED-PROJECT-CONTRACT.md`: rules that apply to every milestone.
- `VERIFIED-SOURCE-LEADS.md`: preliminary class/method findings and public references.
- `milestones/`: one bounded implementation phase per file.
- `templates/milestone-status-template.md`: repository status entry format.
- `templates/final-report-template.md`: final handoff/report format.
- `templates/manual-test-matrix.md`: reusable manual validation matrix.

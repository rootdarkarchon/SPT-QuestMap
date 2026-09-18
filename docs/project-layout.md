# Project layout

The repository root is kept small for people arriving from GitHub:

- `README.md` explains the mod, installation, and normal use.
- `LICENSE` contains the MIT license.
- `AGENTS.md` records repository-specific implementation constraints for coding agents.
- `src/` contains the SPT server/browser mod, shared core, and native client.
- `tests/` contains unit and migration regressions plus sanitized fixtures.
- `scripts/` contains guarded build, deployment, and verification helpers.
- `.github/workflows/release.yml` builds tagged versions against private, hash-pinned SPT 4.1.6 / EFT 40743 references and publishes an install-ready GitHub Release archive.
- `config/` contains example local configuration.
- `reference/` contains the original visual prototype and placeholders for local, ignored SPT sources and profile fixtures.

Internal product and implementation records live in this `docs/` directory:

- `requirements.md` — product behavior.
- `state-model.md` — quest/profile interpretation.
- `architecture.md` — current server, Blazor, and Canvas boundaries.
- `performance.md` — rendering constraints and measurements.
- `blazor-migration.md` — migration findings and implemented outcome.
- `acceptance.md` — acceptance checklist.
- `development-status.md` — concise current operational handoff.
- `quest-actions-plan.md` — historical server action design; native EFT views/controllers now own actions.
- `client-localization.md` — embedded in-game catalog, named-placeholder, and native-tooltip contract.
- `deployment.md` and `testing.md` — contributor workflows.
- `milestones.md`, `open-questions.md`, and `visual-reference.md` — original planning and reference material.
- `project-context.json` — compact machine-readable project facts.

# Scripts

These are intentionally generic starter hooks.

- `build.ps1` builds the first project/solution created under `src/`.
- `deploy.ps1` deploys a prepared directory only after a successful build, compares DLL hashes, and optionally runs the user-provided restart command.
- `verify.ps1` performs a basic HTTP smoke test.

Codex should adapt paths and output conventions after inspecting the exact SPT 4.0.13 source and project layout. Do not weaken the build-before-deploy or DLL-change restart behavior.

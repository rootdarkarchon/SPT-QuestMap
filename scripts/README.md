# Scripts

- `build.ps1 -Target Client|Server|Both` verifies the installed SPT version, uses the SDK's isolated `--artifacts-path` output, runs the relevant tests, and stages clean `dist/client/` and/or `dist/server/` directories.
- `deploy.ps1 -Target Client|Server|Both` invokes that build by default and synchronizes only the requested stage. Client deployment never inspects or stops Tarkov. A changed server DLL requires `-RestartCommand` or the ignored `scripts/restart-command.local.txt` before copying; that configured command runs after deployment.
- `package-release.ps1 -Target Client|Server|Both` creates a tag-versioned install-ready ZIP. `Both` includes `SPT_Runtime/user/mods/SPT-QuestMap/` and `BepInEx/plugins/SPTQuestMap/`; the default remains `Server` for explicit local server-only packaging.
- `verify.ps1` performs a basic HTTP smoke test against `/questmap`.

`build.ps1` and `deploy.ps1` expect `-SptRoot` (or `SPT_ROOT`) to be the Tarkov install directory that contains the `SPT_Runtime` subfolder. Do not include `SPT_Runtime` itself in the supplied path.

These helpers are intentionally conservative: failed builds are never deployed, deployment is limited to the resolved `SPT-QuestMap` directory, and a changed DLL always requires a server restart.

Typical commands:

```powershell
./scripts/build.ps1 -Target Both -Configuration Release
./scripts/deploy.ps1 -Target Client -Configuration Release
./scripts/deploy.ps1 -Target Server -Configuration Release
```

Use `-SkipBuild` only to deploy an already staged target and `-SkipTests` only when explicitly requested. `-DeploymentSource` may identify either the common staging root containing `client/` and `server/`, or a direct single-target stage when `-Target` is not `Both`.

Every completed implementation slice must run the combined `Both` build/deployment validation as applicable and publish a fresh `package-release.ps1 -Target Both` archive. Inspect its entries and record its byte size and SHA-256 in `docs/development-status.md`; do not treat an archive generated before the latest packaged runtime/data change as current release evidence.


`package-build-references.ps1 -SptRoot <root>` creates the private combined 4.1.6/EFT 40743 CI input and SHA-256 manifest under `artifacts/private-build-references/`. The user provisions `EFT_REFERENCE_ARCHIVE_4_1_URL` and `EFT_REFERENCE_ARCHIVE_4_1_SHA256`. These archives must never be published with QuestMap releases.

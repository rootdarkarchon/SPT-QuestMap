# Scripts

- `build.ps1 -Target Client|Server|Both` verifies the installed SPT version, uses the SDK's isolated `--artifacts-path` output, runs the relevant tests, and stages clean `dist/client/` and/or `dist/server/` directories.
- `deploy.ps1 -Target Client|Server|Both` invokes that build by default and synchronizes only the requested stage. Client deployment never inspects or stops Tarkov. A changed server DLL stops only the exact configured `SPT.Server.exe` and relaunches it in a visible console.
- `package-release.ps1 -Target Client|Server|Both` creates a tag-versioned install-ready ZIP. `Both` includes `SPT/user/mods/SPT-QuestMap/` and `BepInEx/plugins/SPTQuestMap/`; the default remains `Server` for explicit local server-only packaging.
- `verify.ps1` performs a basic HTTP smoke test against `/questmap`.

`build.ps1` and `deploy.ps1` expect `-SptRoot` (or `SPT_ROOT`) to be the Tarkov install directory that contains the `SPT` subfolder. Do not include `SPT` itself in the supplied path.

These helpers are intentionally conservative: failed builds are never deployed, deployment is limited to the resolved `SPT-QuestMap` directory, and a changed DLL always requires a server restart.

Typical commands:

```powershell
./scripts/build.ps1 -Target Both -Configuration Release
./scripts/deploy.ps1 -Target Client -Configuration Release
./scripts/deploy.ps1 -Target Server -Configuration Release
```

Use `-SkipBuild` only to deploy an already staged target and `-SkipTests` only for an explicit fast iteration. `-DeploymentSource` may identify either the common staging root containing `client/` and `server/`, or a direct single-target stage when `-Target` is not `Both`.

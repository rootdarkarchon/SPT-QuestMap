# Scripts

- `build.ps1` verifies the installed SPT version, builds QuestMap, runs the tests, and stages a clean `dist/` directory.
- `deploy.ps1` synchronizes a successful build into the resolved QuestMap mod directory, reports whether the DLL changed, and can run an explicitly supplied restart command.
- `package-release.ps1` creates the tag-versioned release ZIP with the install-ready `SPT/user/mods/SPT-QuestMap/` directory structure.
- `verify.ps1` performs a basic HTTP smoke test against `/questmap`.

`build.ps1` and `deploy.ps1` expect `-SptRoot` (or `SPT_ROOT`) to be the Tarkov install directory that contains the `SPT` subfolder. Do not include `SPT` itself in the supplied path.

These helpers are intentionally conservative: failed builds are never deployed, deployment is limited to the resolved `SPT-QuestMap` directory, and a changed DLL always requires a server restart.

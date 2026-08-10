# Build and deployment

Exact paths and commands must be confirmed from the supplied SPT 4.0.13 source and installed server.

## Configuration

Use local, ignored configuration for:

- installed SPT root path;
- resolved mod deployment directory;
- build configuration;
- restart command.

Do not commit machine-specific paths or credentials.

## Required flow

1. Restore/build the project.
2. Run unit tests and any static web checks.
3. Produce a clean deployment directory.
4. Compare the new DLL hash with the deployed DLL.
5. Copy deployment files into the installed SPT mod directory.
   Preserve the user-managed `Summaries/` subtree when removing stale deployment artifacts.
6. If and only if the DLL changed:
   - invoke the user-provided restart command, or
   - clearly report that restart is required if no command is configured.
7. Verify `/questmap` after restart.

## Tagged GitHub releases

Pushing a semantic-version tag such as `2.0.0` runs `.github/workflows/release.yml` on a Windows GitHub-hosted runner. The workflow:

1. verifies that the tag exactly matches the map, shared-core, client, and BepInEx plugin versions;
2. downloads the official `sp-tarkov/build` release archive for SPT 4.0.13;
3. downloads a hash-pinned, access-controlled EFT 40087 build-reference archive;
4. verifies the exact SPT core and `Assembly-CSharp.dll` identities;
5. builds the server and client and runs the complete test suite;
6. creates the combined `SPT-QuestMap-<version>.zip`; and
7. publishes that ZIP on the matching GitHub Release.

Configure `EFT_REFERENCE_ARCHIVE_URL` and `EFT_REFERENCE_ARCHIVE_SHA256` as repository secrets. The private ZIP must contain only the EFT 40087 build-reference layout required by the client project: `EscapeFromTarkov.exe`, `EscapeFromTarkov_Data/Managed/`, `BepInEx/core/`, and `BepInEx/plugins/spt/`. Do not commit or publicly distribute that archive.

The GitHub workflow and a local `scripts/package-release.ps1 -Target Both` package contain both install-root-relative paths:

```text
SPT/
  user/
    mods/
      SPT-QuestMap/
        LICENSE
        SPTQuestMap.dll
        SPTQuestMap.pdb
        SPTQuestMap.Core.dll
        SPTQuestMap.Core.pdb
        Data/
          metainfo.json
BepInEx/
  plugins/
    SPTQuestMap/
      LICENSE
      SPTQuestMap.Client.dll
      SPTQuestMap.Client.pdb
      SPTQuestMap.Core.dll
      SPTQuestMap.Core.pdb
```

The official SPT archive and private EFT reference archive are used only as build references on the runner and are not republished inside the QuestMap archive. The MIT license travels with the binary distribution; installation and usage documentation remain on the GitHub Release and repository README.

## Restart command

The local deployment helper treats `SPT_ROOT` as the Tarkov directory containing both `BepInEx` and `SPT`. When a deployed server DLL changes and the matching server was already running, it stops only that exact `SPT/SPT.Server.exe` process and launches the same executable again. An explicit `-RestartCommand` remains available for other environments.

## Safety

- Do not overwrite unrelated mod folders.
- Do not edit profiles during deployment.
- Do not delete installed SPT files outside the resolved SPT-QuestMap deployment directory.
- Fail the deployment if build/tests fail.
- Log copied files and whether the DLL hash changed.

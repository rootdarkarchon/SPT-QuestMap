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

Pushing a semantic-version tag such as `1.1.0` runs `.github/workflows/release.yml` on a Windows GitHub-hosted runner. The workflow:

1. verifies that the tag exactly matches `<Version>` in `SPTQuestMap.csproj`;
2. downloads the official `sp-tarkov/build` release archive for SPT 4.0.13;
3. verifies `SPTarkov.Server.Core.dll` reports version `4.0.13.0`;
4. builds the mod and runs the complete test suite against those external assemblies;
5. creates `SPT-QuestMap-<version>.zip`; and
6. publishes that ZIP on the matching GitHub Release.

The archive contains the complete install-root-relative path:

```text
SPT/
  user/
    mods/
      SPT-QuestMap/
        LICENSE
        SPTQuestMap.dll
        SPTQuestMap.pdb
```

The official SPT archive is used only as a build reference on the runner and is not republished inside the QuestMap archive. The MIT license travels with the binary distribution; installation and usage documentation remain on the GitHub Release and repository README.

## Restart command

The user will provide the command. Treat it as an opaque shell command and do not invent service names or process paths.

## Safety

- Do not overwrite unrelated mod folders.
- Do not edit profiles during deployment.
- Do not delete installed SPT files outside the resolved SPT-QuestMap deployment directory.
- Fail the deployment if build/tests fail.
- Log copied files and whether the DLL hash changed.

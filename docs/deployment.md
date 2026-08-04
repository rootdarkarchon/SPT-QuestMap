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
6. If and only if the DLL changed:
   - invoke the user-provided restart command, or
   - clearly report that restart is required if no command is configured.
7. Verify `/questmap` after restart.

## Restart command

The user will provide the command. Treat it as an opaque shell command and do not invent service names or process paths.

## Safety

- Do not overwrite unrelated mod folders.
- Do not edit profiles during deployment.
- Do not delete installed SPT files outside the resolved SPT-QuestMap deployment directory.
- Fail the deployment if build/tests fail.
- Log copied files and whether the DLL hash changed.

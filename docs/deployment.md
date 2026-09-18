# Build and deployment

QuestMap 2.1 targets SPT `>=4.1.6 <4.2.0`; the pinned build is SPT 4.1.6 / EFT 40743. Use .NET 10 and matching read-only external references. `-SptRoot` and `SPT_ROOT` identify the Tarkov installation root containing `SPT_Runtime`, `BepInEx`, and `EscapeFromTarkov.exe`.

## Local workflow

```powershell
scripts/build.ps1 -Target Both -Configuration Release -SptRoot D:\Tarkov-SPT-4.1
scripts/package-release.ps1 -Target Both -Configuration Release -Version 2.1.1
scripts/deploy.ps1 -Target Both -SptRoot D:\Tarkov-SPT-4.1 -SkipBuild -RestartCommand '<your configured command>'
```

The build runs shared-core, server/browser, and metadata-only native compatibility tests before staging output. `-SkipTests` requires an explicit user request. A client-only build still runs core and compatibility tests. A server-only build does not require game references.

Packages contain only:

- `SPT_Runtime/user/mods/SPT-QuestMap/`: server/Core assemblies, symbols, license, `config.default.json`, and `Data` catalogs.
- `BepInEx/plugins/SPTQuestMap/`: client/Core assemblies, symbols, and license.

No game/vendor assemblies, raw profiles, copied artwork, or loose server `.js`/`.ts` files belong in release archives. Server and client versions must match. Inspect each fresh combined ZIP for allowed roots and path containment.

## Restart and preservation

When server DLLs change, deployment requires `-RestartCommand` or the ignored `scripts/restart-command.local.txt` before copying either component. The command runs after copying and must handle the intended SPT server instance. No command is inferred, and an unchanged server does not restart. `-WhatIf` previews copying without requiring a command.

Client deployment never inspects or stops EFT. A mapped-DLL lock is a deployment failure, not authorization to kill the game. Only QuestMap's two resolved installation directories may be synchronized; the server's user-managed `summaries/` subtree, `config.json`, and external client configuration/tracking files are preserved. Compare staged and installed hashes after deployment. Do not poll server readiness; the user confirms readiness before runtime checks.

## Browser authentication

Server startup creates `SPT_Runtime/user/mods/SPT-QuestMap/config.json` with `"requireBrowserAuthentication": true` if it is missing. The package also includes `config.default.json`, which can be copied to `config.json` before startup. The default accepts any authenticated SPT user, including non-administrators, and respects SPT's configured localhost bypass. Set the value to `false` in `config.json` and restart the server to permit anonymous access to the read-only browser page and all its sanitized profile views. This changes only QuestMap's policy; native feeds and other SPT pages retain their existing authentication. Invalid configuration logs a warning and keeps authentication enabled. The active `config.json` is generated locally and is not overwritten by release archives; only the default template is shipped.

## Private CI build references

Create one combined reference ZIP from the pinned installation:

```powershell
scripts/package-build-references.ps1 -SptRoot D:\Tarkov-SPT-4.1
```

The ZIP and its SHA-256 manifest are written to ignored `artifacts/private-build-references/`. The archive contains only server DLLs, the EFT executable/managed DLLs, and SPT/BepInEx reference DLLs. It contains no profiles, credentials, logs, or installed third-party mods.

The user provisions an access-controlled download and these GitHub Actions secrets:

- `EFT_REFERENCE_ARCHIVE_4_1_URL`
- `EFT_REFERENCE_ARCHIVE_4_1_SHA256`

GitHub secret names permit underscores, not periods. These are the valid spellings of the requested 4.1-specific names. Never attach the private reference ZIP to a public release.

Tagged CI validates the tag/project/plugin versions, downloads the private ZIP, checks its SHA-256 and the pinned server/EFT assembly hashes, runs the combined build/tests, packages QuestMap, and publishes only the QuestMap release ZIP. Provisioning secrets and executing remote CI remain separate from a successful local build. See [the migration record](spt-4.1-migration.md) for pins and runtime acceptance.

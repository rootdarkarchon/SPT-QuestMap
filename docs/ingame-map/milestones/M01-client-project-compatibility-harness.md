# Milestone 1 — Client Project and Compatibility Harness

## Objective

Create a loadable but behaviorally inert BepInEx client project for the exact SPT 4.0.13 client.

No screen replacement should occur yet.

## Prerequisite

Milestone 0 must be recorded complete, or the exact missing environment facts must already be supplied.

## Required implementation

Create:

```text
src/SPTQuestMap.Client/
```

Suggested structure:

```text
SPTQuestMap.Client.csproj
QuestMapClientPlugin.cs
Configuration/
Compatibility/
Diagnostics/
Patches/
Data/
Graph/
UI/
```

Target:

```xml
<TargetFramework>netstandard2.1</TargetFramework>
```

Use a guarded local root property such as:

```text
EftInstallRoot
```

or:

```text
SPT_ROOT
```

Reference only the exact required installed assemblies. Do not copy them into source control.

The build must fail with useful errors when required paths are absent.

## Plugin startup

Implement:

- BepInEx plugin metadata;
- exact supported EFT/SPT client version validation;
- configuration initialization;
- structured startup logging;
- patch registration infrastructure;
- safe disable behavior when incompatible.

Initial configuration must include at least:

```text
EnableTraderQuestGraph
EnableGlobalTasksGraph
EnableCustomQuestDetails
EnableDebugLogging
```

All replacement features should default to `false` in this milestone.

Historical note: M07 later made custom details and the native-action bridge intrinsic to either enabled Tasks replacement and removed `EnableCustomQuestDetails` as an independent option. The two screen-replacement toggles remain default-off.

## Compatibility diagnostics

Log once at startup:

- plugin version;
- supported EFT/SPT version;
- detected EFT/SPT version;
- relevant assembly identity;
- resolved patch target count;
- unresolved target names;
- whether the plugin is active or safely disabled.

Do not spam logs every frame.

## Patch behavior

Patch classes may resolve target methods for diagnostics, but must not change screen behavior while all features are disabled.

## Verification

Run:

- client project build;
- existing server tests/build;
- in-game startup with all features disabled;
- version mismatch or forced incompatibility handling if practical.

Verify vanilla trader and Tasks screens are unchanged.

## Documentation

Update:

- `docs/in-game-client-design.md` with final project references and compatibility behavior;
- `docs/development-status.md` with milestone status and exact build command.

## Acceptance gate

Milestone 1 is complete when:

- the client project builds;
- its DLL loads in the exact client;
- version validation works;
- feature-disabled behavior is vanilla;
- the plugin safely disables on mismatch;
- the existing server project remains healthy.

When complete, proceed to `M02-graph-model-client-data-adapter.md`.

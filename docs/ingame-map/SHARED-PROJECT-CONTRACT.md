# Shared Project Contract

These rules apply to every milestone of the SPT-QuestMap in-game UI project.

## Mission

Extend `rootdarkarchon/SPT-QuestMap` with a native in-game BepInEx client mod for **SPT 4.0.13**.

The final product should provide:

- a graph-based per-trader Tasks view with that trader applied as the graph filter;
- a global Tasks replacement with **In Progress**, **Quest Map**, **Quest Items**, and **Notes** views;
- a graph-oriented quest detail pane with better blocker, progress, owned-item, handover, prerequisite, and successor visibility;
- native EFT accept, restart, reroll, handover, and complete behavior;
- a working vanilla fallback for every replaced screen;
- no regression to the existing `/questmap` server/Blazor application.

## Read first in every session

Read:

```text
AGENTS.md
README.md
docs/development-status.md
this shared contract
the current milestone file
```

Read additional repository documents only when relevant to the active milestone. Do not load all future milestone files into context.

Inspect the working tree before editing and preserve unrelated user changes.

## Exact runtime target

Target **SPT 4.0.13** only.

Do not add speculative SPT 4.1 compatibility.

Before applying patches, validate the exact installed EFT/SPT client. A mismatch must disable the client mod or fail clearly rather than apply uncertain patches.

Do not commit EFT, Battlestate, Unity, BepInEx, or SPT binary assemblies.

## Source-first rule

The installed client and its matching assemblies are authoritative.

Public decompiles and other mods are investigation aids, not a substitute for verifying the exact local build.

Do not:

- infer behavior merely from obfuscated class names;
- copy current SPT 4.1 APIs into this 4.0.13 project;
- guess a private field or method signature when the local assembly can answer it;
- continue after discovering an installed/source version mismatch.

When source evidence is missing, report the precise missing fact and the smallest action required to obtain it.

## Native Unity UI only

The in-game client must use native Unity UI and C#.

Do not embed:

- Chromium;
- a WebView;
- the Blazor page;
- a browser overlay;
- a JavaScript-to-game transaction bridge.

The browser Canvas renderer is an algorithm and UX reference only.

## Native quest actions only

While EFT is running, every profile-affecting action must go through the verified native EFT client controllers and normal network transaction flow.

Do not:

- edit profile JSON;
- mutate quest status directly;
- mutate inventory directly;
- add QuestMap-specific server mutation endpoints;
- manually construct profile deltas;
- optimistically remove items or grant rewards;
- implement a second accept/handover/complete transport.

The native equivalents of these operations remain authoritative:

```csharp
AcceptQuest(...)
FinishQuest(...)
HandoverItem(...)
QuestChange(...)
```

Prevent double submission and preserve native error behavior.

## Do not mutate the live quest book for display

Use this conceptual model:

```text
raw global/profile quest templates
        +
live QuestClass instances keyed by quest ID
        =
static topology plus live profile overlay
```

Do not call `QuestBook.LoadAll()` merely to expose future nodes unless exact source and runtime testing prove it safe.

Do not inject fake future quests into the live controller.

Template-only future quests may use a QuestMap-owned read-only detail pane.

## Existing server/browser mod remains intact

The existing server project and `/questmap` page remain supported.

Preserve:

- exact SPT 4.0.13 server target;
- read-only browser behavior;
- sanitized profile data;
- existing build and deployment rules;
- graph/state tests;
- current QuestMap behavior unless a deliberate shared-core refactor is verified not to change it.

## Intended project structure

Move toward:

```text
src/
├── SPTQuestMap/
│   └── existing net9.0 server and Blazor application
├── SPTQuestMap.Core/
│   └── runtime-neutral graph contracts, rules, and layout
└── SPTQuestMap.Client/
    └── netstandard2.1 BepInEx client mod
```

`SPTQuestMap.Core` must not reference ASP.NET, Blazor, JavaScript, Unity, BepInEx, SPT server assemblies, or EFT client assemblies.

Do not force a large shared-core extraction before boundaries are proven.

## Safe fallback

Every replacement feature must have a vanilla fallback.

On graph or custom-view initialization failure:

1. log the exception and screen context;
2. dispose partial graph state;
3. restore or retain the vanilla UI;
4. leave close/back navigation functional;
5. avoid stale input blockers or hidden panels.

Provide separate configuration controls for major replacements so a user can disable the trader graph, global Tasks graph, or custom detail pane independently.

## Unity lifecycle and threading

All Unity UI creation and mutation must occur on the Unity main thread.

Every event subscription, coroutine, instantiated object, pooled view, and patch-owned controller must have a clear owner and cleanup path.

Repeated screen opening must not create:

- duplicate graph roots;
- duplicate event subscriptions;
- stale quest references;
- leaked node objects;
- invisible input blockers;
- duplicate actions.

## Data honesty

Unsupported or ambiguous quest conditions must be preserved and marked unknown.

Do not invent:

- objective progress;
- blocker satisfaction;
- item eligibility;
- quest availability;
- reward behavior.

Log unsupported condition types with enough information for later implementation.

## Performance requirements

Keep these concerns separate:

```text
topology
layout
profile/status overlay
selection/highlight overlay
viewport transform
```

Do not:

- rebuild topology every frame;
- recalculate full layout for ordinary status changes;
- create one GameObject per edge;
- perform O(all edges) work on each pointer movement;
- recreate all nodes during pan or zoom;
- continuously poll the profile when events are available.

Use pooled nodes and batched edge rendering for production work.

## Build behavior

The client project should normally target `netstandard2.1` and reference the exact installed client assemblies through `EftInstallRoot` or the shared `SPT_ROOT`. Both identify the Tarkov directory containing the `BepInEx` and `SPT` subdirectories.

Builds must fail clearly when required local references are absent.

Document exact build and test commands as they become stable.

When a deployed server DLL changes, run the configured user-provided SPT server restart command as part of the same deployment workflow. Do not leave the restart for the user, and do not guess a command when none is configured.

## Development discipline

After every milestone:

- run relevant builds and tests;
- update `docs/development-status.md`;
- record exact source-sensitive targets;
- record manual tests completed and pending;
- leave the repository in a buildable state;
- continue to the next milestone unless blocked.

Whenever a new `.csproj` is added anywhere in this repository, add it to `src/SPTQuestMap/SPTQuestMap.slnx` in the same change so solution-wide build and test commands cannot omit it.

Do not mix unrelated browser cosmetic work into client milestones.

## Stop conditions

Stop only for a genuine blocker, such as:

- missing exact assembly or source;
- version mismatch;
- unsafe profile/inventory behavior requiring proof;
- required manual in-game validation before further work.

A blocker report must include:

- exact blocker;
- evidence gathered;
- why it blocks the next step;
- smallest user action required.

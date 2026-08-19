# Milestone 4 — Native Actions and Reactive Updates

## Objective

Prove that trader-screen quest actions remain fully native and that graph state reacts correctly without polling or unnecessary layout rebuilds.

## Prerequisite

Milestone 3 trader graph and native detail selection must work reliably.

## Native action verification

Using the retained native detail pane, verify:

- accept;
- restart;
- ordinary handover;
- partial-stack handover;
- currency handover;
- weapon assembly handover;
- complete;
- repeatable quest reroll where applicable.

Do not duplicate these actions in QuestMap code.

## Event-driven refresh

Identify and subscribe to the minimum verified event set for:

- quest status changes;
- objective progress changes;
- handover completion;
- quest completion;
- linked/new quest availability;
- conditional visibility changes;
- repeatable expiration;
- repeatable reroll;
- relevant trader-state changes.

Keep invalidation categories separate:

```text
topology invalidation
profile/status overlay invalidation
selection refresh
detail refresh
```

Do not rebuild layout for ordinary status or progress changes.

## Selection behavior

Implement deterministic rules:

- accepted quest remains selected;
- partial handover remains selected;
- completed quest remains selected while still visible;
- if filtering removes the selected node, select a sensible nearby node or clear selection;
- newly unlocked quests may highlight but must not steal selection unexpectedly.

## Async safety

Native controls already own much of the action state. QuestMap additions must not cause double invocation.

When adding any bridge callback:

- guard against repeated clicks;
- preserve native busy/error behavior;
- never claim success before the native operation resolves.

## Diagnostics

In debug mode, log concise transitions:

- quest ID;
- old/new exact status;
- overlay refresh reason;
- topology rebuild reason;
- selected-node consequence.

Avoid frame-level or one-second timer spam.

## Manual acceptance tests

Use disposable profile backups and test:

1. accept an available quest;
2. restart a restartable quest;
3. hand over part of a stack;
4. hand over currency;
5. perform weapon assembly handover where possible;
6. finish a ready quest;
7. confirm rewards/messages/linked quests;
8. reroll a repeatable quest where possible;
9. verify no duplicate transactions;
10. verify graph selection and state after every action.

If manual verification is unavailable, leave the milestone `Partial — awaiting manual verification` and provide exact steps and expected observations.

## Documentation

Update:

- native action/event diagrams in `docs/in-game-client-design.md`;
- milestone state and manual results in `docs/development-status.md`.

## Acceptance gate

Milestone 4 is complete when:

- native actions work from the graph-selected quest detail;
- graph state updates event-first;
- no custom profile mutation path exists;
- no duplicate action execution occurs;
- status refreshes do not recalculate topology/layout unnecessarily.

When complete, proceed to `M05-production-renderer-shared-core.md`.

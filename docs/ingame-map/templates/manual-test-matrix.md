# Manual Test Matrix

Use disposable profile backups for all profile-affecting tests.

## Environment

- EFT version:
- SPT version:
- Client mod build:
- Server mod build:
- Resolution/UI scale:
- Other quest UI mods:

## Startup and compatibility

- [ ] Supported version loads.
- [ ] Unsupported version disables safely.
- [ ] All features disabled preserves vanilla UI.
- [ ] Logs identify resolved patch targets.

## Trader graph

- [ ] Open several traders.
- [ ] Correct trader filter is applied.
- [ ] Live quest selection opens details.
- [ ] Future quest selection opens read-only details.
- [ ] Pan/zoom/fit work.
- [ ] Repeated open/close creates no duplicates.
- [ ] Forced failure restores vanilla list.

## Native actions

- [ ] Accept.
- [ ] Restart.
- [ ] Partial handover.
- [ ] Currency handover.
- [ ] Weapon assembly handover.
- [ ] Complete.
- [ ] Repeatable reroll.
- [ ] Linked quest appears.
- [ ] No duplicate action execution.
- [ ] Graph state updates correctly.

## Global Tasks

- [ ] In Progress view.
- [ ] Full Quest Map view.
- [ ] Quest Items view.
- [ ] Notes view.
- [ ] Repeated tab switching.
- [ ] Native quest item transfer.
- [ ] Native Notes CRUD/search.
- [ ] Forced failure restores full vanilla Tasks UI.

## Custom details

- [ ] Locked/future quest.
- [ ] Available quest.
- [ ] Active numerical objective.
- [ ] FIR handover objective.
- [ ] Currency objective.
- [ ] Weapon assembly objective.
- [ ] Ready-to-finish quest.
- [ ] Completed quest.
- [ ] Failed/excluded quest.
- [ ] Repeatable/timed quest.
- [ ] Modded quest.
- [ ] Native fallback works.

## Rendering and persistence

- [ ] Full topology remains responsive.
- [ ] Rapid pan/zoom.
- [ ] Search and filters.
- [ ] Focus chain.
- [ ] Route highlighting.
- [ ] Viewport persists.
- [ ] Selection persists.
- [ ] Status refresh does not visibly rebuild layout.
- [ ] 1920x1080.
- [ ] 2560x1440.
- [ ] Ultrawide if available.

## Server/browser regression

- [ ] Server starts.
- [ ] `/questmap` loads.
- [ ] Profile selection works.
- [ ] Browser refresh works.
- [ ] Existing tests pass.

## Profile safety

- [ ] Profile backup created.
- [ ] Inventory remains synchronized after handover.
- [ ] Rewards apply exactly once.
- [ ] State persists after server restart.
- [ ] No profile corruption observed.

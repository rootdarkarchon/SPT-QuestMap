# Quest actions and inventory synchronization plan

Status: design only. This is a possible post-acceptance extension; the current mod remains read-only.

## 4.0.13 source findings

- The standard EFT action route is `/client/game/profile/items/moving`. `ItemEventStaticRouter` passes an `ItemEventRouterRequest` to `ItemEventRouter.HandleEvents`.
- `QuestItemEventRouter` handles the exact action names `QuestAccept`, `QuestHandover`, and `QuestComplete`, forwarding them through `QuestCallbacks` to `QuestController`/`QuestHelper`.
- Those existing handlers already perform the important mutations and side effects: quest status/counters, inventory removal, reward application, trader changes, mutually failed quests, newly unlocked quests, mail, and response deltas.
- `QuestHandover` accepts a quest ID, condition ID, and selected inventory item IDs/counts. It removes whole item trees when a root item is consumed and changes stack counts for partial handovers.
- The normal item-event route mutates the loaded in-memory profile. `SaveCallbacks` persists profiles periodically (60 seconds in this installation); a web action should additionally call `SaveServer.SaveProfileAsync(profileId)` before acknowledging success.
- SPT's websocket notifications do not expose a general item-event/profile-delta message. `WsProfileChangeEvent` supports specific numeric profile changes, not arbitrary quest status and stash item changes. A browser mutation therefore cannot safely update an already-running EFT client's cached stash through a supported server-only API.
- The server handlers trust several checks normally performed by EFT. For example, accept/complete do not independently prove that every condition is valid, and handover validates target templates but not every client-side eligibility rule. QuestMap must validate actions before invoking the standard router.

## Recommended product boundary

Implement an opt-in **offline profile action mode** first:

- Mutations are disabled by default in mod configuration.
- Read-only QuestMap remains usable exactly as it is now.
- Write endpoints require a separately configured write token sent in a custom header. Read APIs remain under the current unauthenticated local-server policy.
- Reject every write while `SptWebSocketConnectionHandler.IsWebSocketConnected(profileId)` is true. Also warn/reject when `ProfileActivityService` reports very recent activity, covering an abnormal client whose websocket disconnected without exiting.
- The page clearly states that EFT must be closed before accepting, handing over, or completing quests.
- Every action requires a confirmation dialog identifying the profile and irreversible effects. Handovers list the exact stacks/items that will be removed.

This avoids a stale EFT inventory overwriting or visually disagreeing with the authoritative in-memory profile. Supporting writes while EFT is open should be a separate client-companion project, not an optimistic server-only shortcut.

## Server architecture

Add an injectable `QuestMapActionService` which owns validation and orchestration. Controllers should never mutate `PmcData` directly.

### Sanitized inventory/action projection

`GET /questmap/api/profiles/{profileId}/quest-actions?language=<code>` returns only:

- whether mutation mode is enabled and whether an EFT client appears active;
- a profile/inventory revision token;
- quests currently eligible for accept, restart, handover, or completion;
- for each handover condition, remaining count and eligible stash candidates;
- candidate item ID, localized name, template ID, icon URL, stack count, found-in-raid flag, durability summary, and whether consuming it also consumes children;
- localized validation/explanation strings.

Do not expose the raw profile, complete inventory tree, secure-container contents, or unrelated items. Begin with descendants of the profile's main stash root only. Equipment and other inventory roots can be considered later behind an explicit option.

The revision token should hash the fields relevant to the proposed action: quest statuses/completed conditions/counters plus inventory item IDs, template IDs, parents, stack counts, FIR state, durability, and the stash root ID. It is not a security token.

### Mutation endpoints

Use narrow typed endpoints:

- `POST /questmap/api/profiles/{profileId}/actions/accept`
- `POST /questmap/api/profiles/{profileId}/actions/handover`
- `POST /questmap/api/profiles/{profileId}/actions/complete`

Every request contains `questId`, `expectedRevision`, and an idempotency key. Handover additionally contains `conditionId` and explicit `{ itemId, count }` selections. Do not accept arbitrary SPT item-event payloads from the browser.

### Transaction sequence

For each request:

1. Verify feature enablement, write token, valid loaded profile, and allowed origin.
2. Acquire a per-profile `SemaphoreSlim` so two QuestMap actions cannot interleave.
3. Reject if EFT is connected/recently active.
4. Recompute the revision and return `409 Conflict` if the browser used stale quest/inventory data.
5. Recompute authoritative quest state and action eligibility from the current profile and live quest database.
6. Validate every selected item against the current stash and condition.
7. Construct exactly one SPT `ItemEventRouterRequest` containing the relevant `AcceptQuestRequestData`, `HandoverQuestRequestData`, or `CompleteQuestRequestData`, using the standard action constant.
8. Call `ItemEventRouter.HandleEvents(request, profileId)` so SPT/mod routers, output finalization, rewards, mail, inventory deltas, and quest side effects remain authoritative.
9. Treat any critical warning as failure and log the action/result. On success, immediately call `SaveServer.SaveProfileAsync(profileId)`.
10. Return a fresh sanitized quest state, inventory/action projection, new revision, and a human-readable action summary. The browser applies only this response; it does not predict mutations locally.

Keep a short-lived idempotency-result cache per profile so a double click or retried request cannot hand over the same item twice.

## Validation rules

### Accept/restart

- Ordinary accept requires SPT's current client-quest result to mark the quest `AvailableForStart` and QuestMap to find no effective blocker.
- Restart requires the exact restartable-failure state supported by 4.0.13.
- Reject faction, event, excluded, pending, unavailable-trader, level, LL/standing, and prerequisite-gated quests even if a forged request names them.

### Handover

- Quest must be active and the condition must be an incomplete `HandoverItem` condition from that quest's live template.
- Selected items must still exist under the stash root and match the condition's target template list.
- Enforce remaining quantity, stack counts, `onlyFoundInRaid`, dogtag level, durability bounds, and other populated item constraints before calling SPT.
- Show all selected child items/attachments when the SPT handler would consume an entire item tree.
- Never auto-submit. A deterministic suggestion may prefer partial stacks and the fewest destructive whole-item removals, but the user confirms the exact selection.
- Initially exclude `WeaponAssembly`: the 4.0.13 server handler does not contain the complete client-side build validator and accepts a matching root template too loosely. Add it only after a source-backed validator or a client companion can reproduce EFT's checks.

### Complete

- Initially require the exact profile status `AvailableForFinish` and no incomplete supported condition.
- Do not infer completion for unsupported objective types. Under-allowing completion is safer than granting rewards early.
- Let SPT's standard completion path apply rewards, mail, failures/exclusions, delayed quests, and newly available quests.

## Browser behavior

- Add an `Enable actions` section only when the server advertises mutation mode.
- Accept/restart and complete are explicit buttons in the detail pane.
- Handover opens an item picker grouped by required objective. It displays required, already handed over, eligible in stash, selected, FIR requirements, and resulting remainder.
- The confirmation screen includes profile nickname/ID suffix, quest, action, exact removed items, and a warning that it is a profile write.
- Disable the action immediately after submission; retain it disabled until the response or a fresh projection arrives.
- Refresh graph state and the sanitized inventory projection after QuestMap's own successful action. External game/profile changes still refresh only through the existing explicit Refresh button.

## Live-client synchronization phase

If actions must work while EFT is running, create a matching BepInEx client companion for the exact EFT/SPT version. It would need to initiate the standard item event from the EFT client or apply the returned `ItemEventRouterResponse` through EFT's own profile/inventory update machinery. The browser would send a signed intent; the companion would revalidate/display it and make the normal client request, ensuring the response reaches the process that owns the cached stash.

Do not implement live mode until these are proven in-game:

- the supported client API for sending a standard item event;
- main-menu-only enforcement and raid/hideout transition behavior;
- application of quest, inventory, reward, mail, and trader deltas;
- disconnect/retry/idempotency behavior;
- compatibility with Fika or any other component that owns profile synchronization.

## Delivery stages

1. **Read-only inventory projection:** eligible item calculation, localization, icons, revision hashing, and fixtures; no buttons or writes.
2. **Offline accept/restart:** opt-in configuration, write token, active-client guard, idempotency, SPT router call, immediate save, and audit logging.
3. **Offline handover:** explicit stack picker, full eligibility validation, item-tree warnings, stale-revision conflicts, and destructive-action tests.
4. **Offline completion:** conservative ready-to-finish checks and reward/unlock/profile-delta validation.
5. **Optional live companion:** separate design/build/acceptance effort after server-only mode is stable.

Each write stage needs sanitized cloned profile fixtures plus manual tests against disposable profile copies. Back up the selected profile before the first enabled write test.

## Acceptance gates

- No mutation endpoint exists unless the opt-in feature is enabled.
- Forged quest IDs, condition IDs, item IDs/counts, stale revisions, and unavailable actions are rejected without profile change.
- A connected/recently active EFT client prevents server-only writes.
- Double submission causes exactly one action.
- Handover removes only the confirmed count/tree and updates its condition counter.
- Completion applies SPT rewards, mail, unlocks, exclusions, and newly accessible quests exactly once.
- Successful actions are persisted immediately and survive server restart.
- Failed actions return localized errors and do not claim success.
- The browser receives only the new sanitized state, not raw `PmcData` or raw item-event profile changes.

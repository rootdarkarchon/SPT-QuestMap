# Test strategy

## Unit tests

Cover pure state and graph logic with small fixtures:

- effective level inheritance;
- loyalty and reputation bound merging;
- success/failure/status edge interpretation;
- mutually exclusive branches;
- faction filtering;
- active/off-season event filtering;
- `None` event-chain removal;
- next-tier future frontier;
- trader-filter direct-successor context seeded only by available/active/completed quests, with finished/level filtering;
- trader-filter selection matching unfiltered recursive-prerequisite visibility across traders;
- trader-filter merge quests showing only direct unmet prerequisites across traders;
- Daily/Weekly/`Daily_Savage` group inclusion, Daily band normalization, and explicit Scav identity propagation;
- unaccepted repeatables classified as available and elapsed groups classified as expired;
- repeatable search, trader, and finished filtering, default-on persisted calendar visibility, plus selection/details without focused-chain activation;
- hide-finished behavior;
- objective dependency ordering;
- objective rich-text parsing through the same allowlisted sanitizer as descriptions and rewards;
- task counter mapping, including capped exposed progress without changing raw comparator semantics;
- focused-chain closure: recursive predecessors plus direct successors only.
- symmetric profile comparison for state, objective, pending-time, exclusion and faction-applicability differences;
- multi-category comparison filtering, both-sides finished/level boundaries, pair-scoped persistence and repeatable suppression;
- version-three differences-only migration and runtime rendering of the comparison header, category bar, changed-first summary and objective matrix.

## Integration tests

Use sanitized profile fixtures representing:

- new low-level profile;
- mid-progression profile with active quests;
- quest ready to hand in;
- completed mutual-exclusion branch;
- failed/restartable quest;
- high-level profile with Collector progression;
- USEC and Bear variants;
- event-active profile where practical.

## Browser tests

Verify:

- profile selection and persistence;
- refresh without viewport reset;
- resize without viewport reset;
- ordinary selection vs focused-chain action;
- objective details and rich-text formatting for locked and active quests;
- overview-mode highlighting;
- hide-finished and show-all-future controls;
- missing-image fallbacks;
- Daily/Weekly horizontal ordering, countdown/expired labels, divider collapse, and repeatable selection;
- keyboard/focus accessibility for major controls.
- profile A/B selection and swapping, all-change and category filtering, dimmed matching quests, cross-faction N/A states, objective deltas, collapsed matching details and responsive comparison controls.

## Performance tests

Test the complete graph. Record layout/render/update timings and inspect browser performance traces for long tasks during:

- initial load;
- pan/zoom;
- selection;
- profile refresh;
- show-all-future toggle;
- focus-chain layout;
- hide-finished toggle.

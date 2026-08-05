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
- hide-finished behavior;
- objective dependency ordering;
- task counter mapping;
- focused-chain closure: recursive predecessors plus direct successors only.

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
- objective details for locked and active quests;
- overview-mode highlighting;
- hide-finished and show-all-future controls;
- missing-image fallbacks;
- keyboard/focus accessibility for major controls.

## Performance tests

Test the complete graph. Record layout/render/update timings and inspect browser performance traces for long tasks during:

- initial load;
- pan/zoom;
- selection;
- profile refresh;
- show-all-future toggle;
- focus-chain layout;
- hide-finished toggle.

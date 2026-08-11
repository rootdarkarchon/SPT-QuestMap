# In-game client localization

QuestMap-owned text in the BepInEx client is stored in embedded JSON catalogs under
`src/SPTQuestMap.Client/Localization/Locales/`. `en.json` is the complete fallback catalog.
The loader first looks for a catalog matching EFT's current language code and then falls
back per key to English. Missing keys are displayed as their semantic key so an incomplete
translation remains diagnosable.

Catalog keys describe meaning rather than their current English wording. Runtime UI text
belongs in the catalog; internal GameObject names, persistence keys, route and quest IDs,
compatibility guards, exception text, and diagnostic log tokens remain invariant code.
Pure control iconography such as `+`, `−`, `×`, arrows, stars, and checkmarks is likewise
not translated.

BepInEx configuration sections, setting names, and descriptions deliberately stay as
invariant literals in `QuestMapClientConfiguration`. Section/name pairs are persistent
`.cfg` identities, so translating them would create language-dependent keys and make
existing settings appear to disappear. Keeping their descriptions beside those identities
also prevents the F12 metadata and the persisted configuration contract from drifting.

Formatted text uses named placeholders:

```json
"tooltip.openDetails": "Open details for {quest}.",
"common.progress": "{current:0.##} / {required:0.##}"
```

Callers pass names with `ClientLocale.Arg(...)`; do not embed quest names, counts, levels,
or other runtime values in resources. Standard .NET numeric formats may follow a colon.
Unknown placeholders remain visible, which makes catalog/caller mismatches detectable.

All QuestMap-created pointer controls bind EFT's native `SimpleTooltip`. The common binder
uses a 0.35-second delay, a 420-pixel maximum width, follows EFT's normal cursor placement
and cleanup, and resolves its text when hover begins. Toggle tooltips therefore describe
the action that the control will perform in its current state. Active view/detail tabs omit
their redundant tooltip. Disabled selection-dependent controls use the shared concise
`tooltip.noQuestSelected` text. Tracking tooltips must describe the live manual and implicit
favorite/current-map policies, including cases where removing manual tracking cannot make
the quest untracked.

## Adding a language

1. Copy `en.json` to the exact EFT language code, for example `ge.json`.
2. Keep every key and named placeholder; translate values and reorder placeholders as the
   language requires.
3. Parse the JSON, build the exact-version client, and verify that all literal
   `ClientLocale.Text`/`ClientLocale.Format` keys exist in the English fallback catalog.
4. Check the Tasks table, graph controls, details tabs/actions, F12 settings, and raid
   overlays in game. Verify both states of every toggle tooltip.

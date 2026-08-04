# Existing graph reference

`reference/existing-quest-graph/index.html` is the latest static quest graph prototype from the design discussion.

Useful ideas to retain where they still fit:

- left-to-right dependency progression;
- vertical effective-level bands;
- quest artwork as the card background;
- trader portrait and name on the left side of each card;
- status-sensitive edge colors;
- recursive prerequisite plus direct-successor selection highlighting;
- focused-chain action that preserves zoom and node screen anchor;
- overview rendering at low zoom;
- local browser persistence;
- faction/event/Collector-derived graph metadata patterns.

Do not treat its implementation as production architecture. It was built as a standalone HTML experiment and had repeated performance iterations. The server mod should reimplement the relevant UX using a renderer and data flow appropriate for live profile state.

The production page must not require copied `quests.json`, `en.json`, quest icons, or trader portraits alongside the page.

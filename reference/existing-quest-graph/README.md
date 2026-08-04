# Tarkov Quest Node Graph

A fully static, client-side visualization of SPT quest dependencies.

## Files

- `index.html` — graph viewer
- `quests.json` — SPT quest templates
- `en.json` — English locale data
- `traders/` — trader portraits named with lowercase trader slugs, e.g. `prapor.png`, `btr-driver.png`, plus `all.png`
- `questicon/` — quest images using the filenames referenced by `quests.json`

## Run locally

Browsers normally block `fetch()` from a directly opened `file://` page. Serve this directory over HTTP, for example:

```powershell
python -m http.server 8080
```

Then open `http://localhost:8080/`.

If automatic loading is blocked, the page also offers manual file pickers for `quests.json` and `en.json`.

## Graph semantics

- Starting quests begin in column 0 of their actual effective-level band; only ungated/level-0/level-1 roots occupy the level-1 band.
- Successors progress left-to-right.
- Vertical bands group nodes by inherited/effective level requirement; no-gate, level-0, and level-1 quests share one level-1 band.
- Green connection: prerequisite quest must succeed.
- Red connection: prerequisite quest must fail.
- Amber connection: either success or failure unlocks the target.
- Blue/teal connections represent started-state variants.
- Direct level requirements are used to compute effective levels but are not separately displayed. Level bands use large numeric labels, with starting/no-gate quests treated as level 1.
- Event, faction, Collector, secret, and trader filters are handled entirely in the browser.
- Clicking a card highlights its full prerequisite closure and direct successors; the eye button rebuilds a compact view containing exactly that subgraph while preserving the current zoom and the eye-selected card’s screen position.
- Quest artwork retains its original color and fills each card background under a neutral darkening layer; the trader portrait and name occupy the left column. Collector and ordinary cards use only their slim purple/green left rails in full-card mode.
- Inherited trader loyalty and reputation requirements appear as compact tags such as `Prapor LL2` and `Fence Rep <= -1`.
- Nodes sharing a column are ordered with quests having no inherited trader gate first, then by trader order, then quest name.

## Performance overview mode

Below 42% zoom the viewer automatically replaces full image cards with lightweight colored node blocks and removes arrow markers. Full cards return automatically when zooming in. Pan/zoom updates are animation-frame throttled, and hover highlighting only touches directly connected edges. Selection highlighting remains visible in overview mode.

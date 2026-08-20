# Claude Design lane packets

**Generated. Do not edit these by hand** — edit [`../SCREEN_DESIGN_BRIEF.md`](../SCREEN_DESIGN_BRIEF.md)
and regenerate:

```
python3 docs/design-packets/generate.py
```

## What they are

The brief is one 42 KB document describing 39 layouts plus 17 overlays. That is too much for a single
Claude Design session, and §4 already splits the work across seven canvas lanes. Each packet is one lane
as a **standalone prompt**: the full shared constraint set (§1–§5, §7, §9, §10) plus exactly that lane's
screens. Every packet carries the whole constraint set, so no lane is ever designed unconstrained.

## Order

Run **Lane 0 first.** It designs the shared component inventory and emits the token block described in
§5.1 — the UI chrome palette, the type scale, the spacing ramp, and the nine perk-category hexes. None of
those values exist anywhere in this repo, by design: the design agent decides them once, on the inventory
artboard.

Paste that emitted block into the `Locked tokens` slot at the top of each later packet before running it.
Lanes 1–6 are independent of each other once they have it, so they can run in any order or in parallel.

Skipping this is the one failure mode that wastes real work: separate sessions invent separate greys, and
lanes come back not matching.

| Packet | Covers |
|---|---|
| `lane-0-system.md` | Component inventory, colour/type sheet, rarity frames — **run first** |
| `lane-1-run-loop.md` | S05 Board · S06 Battle · S07 Perk Draft · tile cards · Stage Gate · S10 · S13 · S14 |
| `lane-2-entry-and-hub.md` | S01 · S02 FTUE · S03 Home · S04 Chapter Select · S28 · S29 |
| `lane-3-character-and-collection.md` | S15 Hero · S16 Inventory · S17 Forge · S18 Talents · S19 Menagerie |
| `lane-4-social.md` | S20 Arena · S21 PvP Loadout · S22 Leaderboard · S33–S36 Guilds |
| `lane-5-service.md` | S23 Shop · S24 Codex · S38 Feats · S25 · S37 · S26 · S27 · S30–S32 Events |
| `lane-6-overlays.md` | The 17 unnumbered surfaces in §8 |

## Two things the packets deliberately leave out

- **Icon sheets.** §5 asks for 50 misc + 12 status + 9 currency icons and 6 die faces. Those come from the
  image pipeline in [`../../game-design/22_ICON_PROMPT_TABLES.md`](../../game-design/22_ICON_PROMPT_TABLES.md),
  not from a layout agent. Use placeholders in Claude Design and skip the icon-sheet artboard.
- **Art.** §3.1's rule that text is never rendered inside a generated image still holds. Claude Design sets
  real type; character and biome art is a separate pipeline.

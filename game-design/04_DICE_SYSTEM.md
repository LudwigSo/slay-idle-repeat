# 04 — The Dice System

The die moves the hero and does nothing else. It is **an ordinary six-sided die**: you roll it, you get a number from 1 to 6, and you walk that many nodes. What happens when you get there is the board's business, not the die's.

⚠️ **This document used to describe the opposite**, and the change is the largest single reduction in the design set: the die was the game's signature mechanic, a piece of equipment upgraded through six face kinds, five upgrade sources and a reroll economy. All of it was removed — see §5 for exactly what, and `16_DECISION_LOG.md` D41 for the ruling. What is left is this page.

---

## 1. The die

The player rolls **one die with six sides**, showing 1 to 6. There is no face type, no face tier, and nothing anywhere in the game that can change what a side shows.

```
[1] [2] [3] [4] [5] [6]
```

A roll answers a number. The number is the movement. Nothing else about a roll is a decision or a modifier.

🔒 **The board is what makes a roll interesting.** A 6 is not better than a 2 in the abstract — it is better or worse depending on what sits six nodes ahead, which is a thing the board decides and the player can read. That is where the game's texture now lives; see `03_BOARD_AND_TILES.md`.

### The one thing that moves a roll

`CUR_SLIPPERY` (`19` Part E) applies **−1 to every roll, to a minimum of 1**. It is a curse, it is the only modifier of any kind on a roll, and the floor of 1 is what stops a cursed run standing still.

---

## 2. Randomness

The draw is **uniform** over the six sides, straight off the run's `dice` RNG stream. The same seed and the same number of previous draws always produce the same roll, which is what makes a run replayable and a command log verifiable.

⚠️ **There is no luck smoothing.** A weighted "Fair Dice" bag used to sit here — a decaying weight vector that made repeated faces rare and was disclosed in the settings menu as *"Fair Dice: ON"*. It went with the rest of the dice system: an ordinary die is ordinary, streaks included, and a settings row promising otherwise would have to be removed too.

🔒 Note for PvP: Ghost Duels contain **no dice**. Dice affect board movement only, never combat. See `11_PVP_GHOST_DUEL.md`.

---

## 3. Presentation

- The die is a chunky 3D-look 2D sprite rendered with a squash-and-stretch tumble, **0.8 s**, landing with a bounce and a small dust puff.
- The result is echoed as a large floating number above the hero token before movement begins.
- Haptics: light tap on roll start, medium on land.
- The board screen shows the number the last roll came up, until the next roll replaces it.

📐 TUNABLE: animation timings must be reducible via the "Fast Mode" setting to 0.25 s.

⚠️ **Six sides means six artworks, not eleven.** The per-face art (`Star` gold with rays, `Surge` cyan, `Fortune` green, `Void` black, `Chain` orange) is gone with the faces, and so is the `Star`/`Fortune` heavy haptic. `15` §E16's count moves with it.

🔒 **No die skins in v1.** All cosmetic rewards were cut (decision D14), so there is exactly one die design.

⚠️ **Flagged for later:** die skins remain a cheap and thematic cosmetic — roughly six assets per skin and no system changes anywhere else. See `16_DECISION_LOG.md` R3.

---

## 4. The player-facing surface

One control: the roll button, the largest interactive element on the board screen and inside the thumb zone (`13` §3). It is a single tap and it is final.

⚠️ **There is no Die Panel.** Screen S12 showed the current six faces with the source that granted each, because *a hidden die is a hostile die* — a real concern when talents, mounts, perks, forge upgrades and curses could all rewrite a face. A die that is always 1..6 has nothing to disclose, so the screen is gone rather than emptied. `13` §1's screen register moves with it.

⚠️ **There is no reroll prompt.** A 4-second ring used to run around a `REROLL (n)` button after the die settled, with a tap-anywhere-else acceptance and an accessibility setting that removed the timer. All of it is gone: with no reroll to offer there is no window to offer it in, and a roll is committed the moment it is taken.

---

## 5. What was removed, and where it went

Recorded here rather than deleted silently, because most of it is referenced from other documents and because some of it is worth rebuilding differently.

| Removed | What it was | Where its absence is now visible |
|---|---|---|
| `DieFaceKind` — `Star`, `Surge`, `Fortune`, `Void`, `Chain` | Five special faces beside `Pip`: choose-your-movement, move-and-heal, move-and-double-the-tile, stay-and-re-resolve, move-and-roll-again | This document; `19` Part E's `CUR_LEADFOOT`; `17` §9's Dicelord |
| Face `Tier` (0..3) | Scaled a face's non-movement effect | This document |
| The five upgrade sources | Talent tree Fortune branch (`09` §6), mount face grants (`07`), `TILE_DICE_FORGE`, run perks, curse downgrades | `09_TALENT_TREE.md`; `07_HERO_PETS_MOUNTS.md`; `03` §2's tile list |
| **Reroll charges** | 1/stage base, +2 from a Campfire choice, +2 from talents, perks, the Reroll Token consumable, `AD_REROLL_DICE`, cap 5 | `03` §2's campfire; `03` §7.1's consumables; `12` §4.1's ad placements |
| **Nudge** | A talent-granted ±1 on a roll, 1/stage | `09_TALENT_TREE.md` |
| **Fair Dice** | The weighted-bag smoothing and its settings row | §2 above; `13` §8's settings list |
| The Die Panel (S12) | The screen that disclosed the composed die | §4 above; `13` §1 |
| 12 Dice & Board perks | `PK_LOADED_DIE`, `PK_SECOND_THOUGHT`, `PK_MOMENTUM_DIE`, `PK_FORTUNES_FAVOUR`, `PK_CHAINBREAKER`, `PK_TWIN_FATES`, `PK_WEIGHTED_FATE`, `PK_DICELORD_GIFT` and the rest | `06_PERKS.md` — the pool is short by a category's worth of rows |

### 5.1 The Dice Forge tile is kept, and does nothing

`TILE_DICE_FORGE` stays in `03` §2's fourteen tile kinds. Its mechanic — pick a face, upgrade it for the run — has nothing left to act on, so **landing on one resolves in place and grants nothing.** It is held for a repurposing, and until that lands it is a tile the player walks over.

🔴 A tile that does nothing is a real hole in the board's reward texture, not a neutral placeholder: it occupies one of the `Arcane` fork's three outcomes (`03` §3.1). Whatever replaces it is owed a design section here or in `03`.

### 5.2 What the removal did not touch

- The `dice` RNG stream, its per-run seeding and its draw accounting. One roll is still exactly one draw.
- The Stage Gate's heal. The gate used to also refresh reroll charges and re-anchor the Fair-Dice bag; the heal is now all it does.
- `CUR_SLIPPERY`. It reads a roll's number, which a plain die still has.
- The **perk draft's** reroll (`REROLL_DRAFT`, Draft Tokens, `AD_REROLL_PERK`, the free-reroll count). A different mechanic that happens to share a word, and untouched throughout.

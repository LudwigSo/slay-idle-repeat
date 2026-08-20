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

**Two controls.** The roll button — the largest interactive element on the board screen and inside the thumb zone (`13` §3), a single tap, and final. And the **fixed-die tray**, which the player may spend instead of rolling; see §6.

🔒 **The board is completely visible at all times.** There is no fog, no preview range and no reveal distance: every tile of every stage is drawn from the moment the run starts. This is load-bearing rather than a convenience — a die that only answers a number is only an interesting decision if the player can see what the numbers reach, and it is the entire reason a *fixed* die is worth choosing a number for.

⚠️ A tile preview range used to be authored content: a `TILE_PREVIEW` stat and a `REVEAL_TILES` op that perks, a talent rank, an event outcome and `CUR_BLIND` all moved. None of it was ever read by anything, and all of it is removed rather than left as a gate nobody may build — see `16` D42, and `19` Part E for the curse.

⚠️ **There is no Die Panel.** Screen S12 showed the current six faces with the source that granted each, because *a hidden die is a hostile die* — a real concern when talents, mounts, perks, forge upgrades and curses could all rewrite a face. A die that is always 1..6 has nothing to disclose, so the screen is gone rather than emptied. `13` §1's screen register moves with it.

⚠️ **There is no reroll prompt.** A 4-second ring used to run around a `REROLL (n)` button after the die settled, with a tap-anywhere-else acceptance and an accessibility setting that removed the timer. All of it is gone: with no reroll to offer there is no window to offer it in, and a roll is committed the moment it is taken.

---

## 5. What was removed, and where it went

Recorded here rather than deleted silently, because most of it is referenced from other documents and because some of it is worth rebuilding differently.

| Removed | What it was | Where its absence is now visible |
|---|---|---|
| `DieFaceKind` — `Star`, `Surge`, `Fortune`, `Void`, `Chain` | Five special faces beside `Pip`: choose-your-movement, move-and-heal, move-and-double-the-tile, stay-and-re-resolve, move-and-roll-again | This document; `19` Part E's `CUR_LEADFOOT`; `17` §9's Dicelord |
| Face `Tier` (0..3) | Scaled a face's non-movement effect | This document |
| The five upgrade sources | Talent tree Fortune branch (removed entirely — `16` D54), mount face grants (`07`), `TILE_DICE_FORGE`, run perks, curse downgrades | `09_TALENT_TREE.md`; `07_HERO_PETS_MOUNTS.md`; `03` §2's tile list |
| **Reroll charges** | 1/stage base, +2 from a Campfire choice, +2 from talents, perks, the Reroll Token consumable, `AD_REROLL_DICE`, cap 5 | Replaced: every one of those grant sites now grants a **fixed die** instead (§6.4). The two that are not re-pointed — a gear affix and the Fateweave set bonus — are §6.4's open holes. |
| **Nudge** | A talent-granted ±1 on a roll, 1/stage | `09_TALENT_TREE.md` |
| **Fair Dice** | The weighted-bag smoothing and its settings row | §2 above; `13` §8's settings list |
| The Die Panel (S12) | The screen that disclosed the composed die | §4 above; `13` §1 |
| 12 Dice & Board perks | `PK_LOADED_DIE`, `PK_SECOND_THOUGHT`, `PK_MOMENTUM_DIE`, `PK_FORTUNES_FAVOUR`, `PK_CHAINBREAKER`, `PK_TWIN_FATES`, `PK_WEIGHTED_FATE`, `PK_DICELORD_GIFT` and the rest | `06_PERKS.md` — the pool is short by a category's worth of rows |

### 5.1 The Dice Forge tile is the fixed-die tile 🔒

`TILE_DICE_FORGE` stays in `03` §2's fourteen tile kinds, and **granting one fixed die is its design** — not a stopgap awaiting a repurposing (`16` D53). Landing on one grants a fixed-die choice: the player names a number 1..6 and holds a die that moves exactly that far.

🔒 **The tile keeps its name and its icon.** A forge that hands you a die with your number on it is the same fiction the face-upgrade version had — you leave with a better die than you arrived with — so nothing about the art, the icon or the `Arcane` fork's risk profile needs to change. The fork's three outcomes are all real again.

⚠️ **It is the only grant site that is a tile**, which makes it the board's one reliable source of guaranteed movement. 📐 If the frequency band (`03` §2.1, weight 2) turns out to make fixed dice too rare to plan around, the weight is the lever — not the grant size.

### 5.2 What the removal did not touch

- The `dice` RNG stream, its per-run seeding and its draw accounting. One roll is still exactly one draw.
- The Stage Gate's heal. The gate used to also refresh reroll charges and re-anchor the Fair-Dice bag; the heal is now all it does.
- `CUR_SLIPPERY`. It reads a roll's number, which a plain die still has.
- The **perk draft's** reroll (`REROLL_DRAFT`, Draft Tokens, `AD_REROLL_PERK`, the free-reroll count). A different mechanic that happens to share a word, and untouched throughout.

---

## 6. Fixed dice

A **fixed die** is a die with one number written on it. Spending one moves the hero **exactly that many nodes**, and the die is gone.

It is the answer to the question an ordinary die raises: if the board is the interesting part and the roll is just a number, the player is a spectator to the one input the game has. A fixed die is the input. It is not a reroll — nothing is re-drawn and no result is replaced — it is a **second, deterministic movement command** the player may take instead of rolling.

### 6.1 The two ways to move

| | Roll | Spend a fixed die |
|---|---|---|
| Result | uniform 1..6 off the `dice` stream | exactly the number on the die |
| Cost | free, always available | consumes the die |
| RNG | one draw | **zero draws** |
| `CUR_SLIPPERY` | −1, floor 1 | **does not apply** |
| Command | `ROLL_DICE` | `USE_FIXED_DIE { pips }` |

🔒 **A curse does not shorten a fixed die.** `CUR_SLIPPERY` takes 1 off a *roll* and deliberately not off this. A fixed die's whole promise is *this many steps*, and a curse that silently broke it would make the one dependable tool in the game undependable — the player would have to remember which curses they carry before reading their own dice.

🔒 **Spending one takes no draw from the `dice` stream.** A deterministic move that consumed randomness would shift every later roll of the same seed for no reason, which is a replay divergence with no cause.

### 6.2 The player picks the number

A grant does not hand over *a 3*. It hands over **a choice**, which the player answers with `CHOOSE_FIXED_DIE { pips }`, naming any number 1..6.

That is the whole design: the reward is worth something because the player decides what it is worth, having looked at the board. Rolling a granted 3 that lands on a Trap is not a reward.

The choice is **owed and persisted**, not resolved at the grant, because most grant sites carry no command a number could ride on — an event outcome is drawn by weight, a minigame reward is decided by play, and an ad reward and a set bonus are entirely passive. A run therefore holds two things: the dice it owns, and the choices it still owes.

⚠️ **An owed choice blocks nothing.** Unlike a pending perk draft, which refuses every other run command until answered, a fixed-die grant can land mid-shop-visit and the player may keep rolling with it outstanding. Interrupting them to name a number would be the worse trade.

### 6.3 Holding them

A run's dice are a **multiset, uncapped**: two dice showing 3 are the same holding twice, so a count is the whole truth. Nothing is per-stage, nothing refreshes, and nothing expires — a die held at the boss is a die spent at the boss.

⚠️ **Uncapped is a decision, not an omission.** The recommendation was a cap of 4 (the consumable cap's precedent). It was overruled: a cap turns every grant past the ceiling into a silently wasted reward, and the grant rate is low enough that hoarding is a plan rather than an exploit. 🔴 If hoarding does turn out to dominate, the cap is the lever, and it belongs here.

### 6.4 Where they come from

Every grant site below is one the reroll used to own. That is deliberate — the reroll's grants were the design's already-balanced answer to *how often should the player get a small movement favour*, and re-pointing them costs nothing a new economy would have to re-derive.

| Site | Grant | Where |
|---|---|---|
| `TILE_CAMPFIRE`, third option | 1 choice | `03` §2 |
| `TILE_DICE_FORGE` | 1 choice | §5.1 — the tile's design, permanently |
| `CON_FIXED_DIE_TOKEN` | 1 choice, instant at the till | `03` §7.1 |
| Shop tile, slot 2 | sells the token | `03` §7 |
| Minigame rewards | 1 choice on a win | `03` §6 — **not** chapter-scaled, and across **three** minigames (`16` D58) |
| Resource dungeons | 1 choice | `25` §3 |
| `AD_FIXED_DIE` | 1 choice, 2 per run | `12` §4.1 |

🔴 **One grant site the reroll had is still empty**: a gear affix. It would need new effects-DSL stat vocabulary — a fixed die is a *held object*, not a stat, and `18` has no way to say "grant one of these". It is not wired and does not pretend to be. ⚠️ The **Fateweave set bonus** was the second such site and is no longer a hole of any kind: the set is removed (`16` D56).

🔒 **Three event-card outcomes (`19` Part A) grant them too.** The board-events vocabulary gained a sixth op, `FIXED_DIE { count }`, for exactly this: three outcomes granted reroll charges (+2, +1, +1) and were left reading as *deferred* long after the mechanic replacing the reroll existed. The authored amounts are carried across rather than flattened.

⚠️ **Two more event outcomes stay unpayable, and for a reason that is not the die's.** `EVT_TAX`'s *pay 15% of current Gold, then +1 charge* and `EVT_STORM`'s *lose 1 charge to heal 20%* both need something else: a proportional cost the flat cost field cannot express, and a way to take a die back — which a card cannot have, because it does not know which of the player's dice it gave them. Paying either reward without its cost would make the option strictly good, so neither half is applied.

🔒 **And three event OPTIONS are gone, deleted rather than deferred.** `EVT_OLD_SOLDIER`'s *Ask about the road*, `EVT_ARCHIVE`'s *Read the maps* and `EVT_LAST_LAMP`'s *Take it* each revealed tiles, which §4 makes impossible — the board is already wholly visible — so there is nothing there to defer. They are **not replaced**: those three cards offer two options each now. A card listing an option that pays nothing asks the player to make a choice that is not one, which is worse than a shorter card. See `19` Part A.


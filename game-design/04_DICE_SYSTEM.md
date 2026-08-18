# 04 — The Dice System

The die is the game's signature. It is not a random number generator the player suffers — it is **a piece of equipment the player upgrades**. This is the mechanic that distinguishes Slay Idle Repeat from every other auto-battler in the genre, so it gets its own systems budget.

---

## 1. The die

The player owns **one die with six faces**. Each face is a `DieFace` value.

```csharp
public enum DieFaceKind { Pip, Star, Surge, Fortune, Void, Chain }

public readonly struct DieFace {
    public DieFaceKind Kind;
    public int  Value;      // pips for Kind == Pip; ignored otherwise
    public int  Tier;       // 0..3 — scales the face's non-movement effect only
                            // (e.g. Surge heals 12% at T0, 15/18/21% at T1-3).
                            // Purely mechanical; there is no cosmetic tier (D14).
}
```

### Starting die
```
[1] [2] [3] [4] [5] [6]      // all Kind = Pip
```

### Face kinds

| Kind | Symbol | Effect on roll |
|---|---|---|
| `Pip` | 1–6 | Move that many nodes. The baseline. |
| `Star` | ★ | **Choose** your movement, 1–6. The most valuable face in the game. |
| `Surge` | ⚡ | Move 3, then heal 12% Max HP. |
| `Fortune` | ✦ | Move 4, and the tile you land on pays double (Gold, Crowns, drops — not perks). |
| `Void` | ○ | Move 0. Stay in place and immediately re-resolve the current tile at 50% reward. Appears only as a **curse-inflicted** face, never as an upgrade. |
| `Chain` | ⛓ | Move 2, then roll again immediately (chains up to 3 times, then forced to stop). |

📐 TUNABLE: all values above.

---

## 2. Where die faces are upgraded

| Source | Scope | Notes |
|---|---|---|
| **Talent tree — Fortune branch** | Permanent | *Weighted Faces*: `1` → `2` at rank 1, `3` at rank 3, `4` at rank 5. *Surging Fate*: `2` → `Surge` at rank 3. Keystones: *The Sixth Star* (`6` → `Star`), *Chainweaver* (`4` → `Chain`), *Golden Fate* (`5` → `Fortune`). See `09_TALENT_TREE.md` §6 — these are the tree's most expensive nodes. |
| **Mounts** | Permanent while equipped | Certain mounts grant a face change (e.g. Starhoof Stag: `3` → `Fortune`). |
| **`TILE_DICE_FORGE`** | Run-scoped | Pick one face, upgrade it for the rest of this run. |
| **Perks** | Run-scoped | e.g. *Loaded Die* (+1 to all Pip faces), *Twin Fates* (Star faces trigger twice) |
| **Curses / Chapter 8** | Temporary | Faces can be *downgraded* or scrambled into `Void` |

The full die, with all sources applied, is recomputed at run start and displayed in a **Die Panel** the player can open at any time during a run. Transparency here is essential — a hidden die is a hostile die.

---

## 3. Reroll charges

| Property | Value |
|---|---|
| Base charges per stage | 1 📐 TUNABLE |
| Refresh | On each Stage Gate (charges do not carry over) |
| Sources of extra charges | Talents (Fortune branch, up to +2), Campfire choice (+2 this stage), perks, the Reroll Token consumable — **+1 charge granted immediately on purchase**, never held; greyed out at the max-stored cap (`03` §7.1, ruled in `16` A7) |
| Ad reroll | `AD_REROLL_DICE`, **2 per run**, does not consume a charge |
| Max stored | 5 |

A reroll re-rolls the die completely — it is not a "+1 nudge". A separate talent grants **Nudge** (±1 to a Pip result, 1/stage), which is a different, cheaper tool.

### 3.1 What a reroll actually changes 🔒 (ruled at the M7 kickoff, D6, 2026-08-18)

🔒 **A reroll changes the NEXT roll. It does not undo the roll it is offered beside, and it never could.**

`ROLL_DICE` answers three questions in one command — the face, the movement it buys and the tile the run lands on — so by the moment a face is on screen the run has **already moved**. There is nothing left to undo. `USE_REROLL` instead burns one draw of the dice stream, advancing the Fair-Dice bag exactly as a real roll would but without moving the run, so the very next `ROLL_DICE` draws a different index against updated weights.

⚠️ **This section previously implied the other reading, and the document is what yielded.** The alternative was splitting `ROLL_DICE` into a roll and a commit, which would cost a 53rd and 54th command against `14` §2.3's frozen vocabulary (another logged `16` decision), change RNG consumption on the dice stream, and diverge **every saved command log** — the same replay blast radius that kept M4-17 out of the M4 review. The shipped command is correct; the sentence describing it was not.

Consequences that follow, and are **not** changed by this amendment:
- The charge is spent when the reroll is taken, and it buys an effect on the next roll rather than a redo of this one.
- The counts, sources and caps in the table above are untouched, tunable markers included — this amendment is about what a reroll *does*, not about any number.
- The 4-second ring and its lapse behaviour are untouched, and are already shipped correctly: **on lapse the roll is accepted and no charge is spent.**

### Reroll prompt UX
After the die settles, a 4-second ring timer runs around a `REROLL (2)` button. Tapping anywhere else or letting the timer lapse accepts the roll. The timer must be skippable and its duration must respect an accessibility setting that removes it entirely (see `13_UI_UX_SCREENS.md` §8).

🔒 **The button's wording must not promise an undo** (§3.1). It offers to change the next roll, and the caption beside it says so — a bare *"Reroll"* on a screen where the run has already moved is read as a redo, which is the one thing the command cannot do.

---

## 4. Randomness fairness

Pure uniform randomness on a 6-sided die produces streaks that players read as broken. Slay Idle Repeat uses a **lightly smoothed distribution**, disclosed in the settings menu ("Fair Dice: ON").

```
Algorithm: weighted-bag with decay
  - Maintain a weight vector w[6], initialised to 1.0 for each face.
  - On roll: pick face f with probability w[f] / sum(w).
  - After rolling f:  w[f] *= 0.55  ; all other faces w[i] += 0.12
  - Clamp each w[i] to [0.25, 2.0].
  - Reset the bag at each Stage Gate.
```

Effect: the same face three times in a row is rare; the expected value stays at 3.5; no face is ever impossible. 📐 TUNABLE (0.55 / 0.12 / clamps).

**The player-facing wording must be honest.** Call it "Fair Dice — reduces long streaks", not "true random".

🔒 Note for PvP: Ghost Duels contain **no dice**. Dice affect board movement only, never combat. This means PvP is entirely free of the dice smoothing question. See `11_PVP_GHOST_DUEL.md`.

---

## 5. Dice-related perks (subset of the perk pool)

These live in the main perk pool (`06_PERKS.md`) but are listed here for cohesion:

| ID | Perk | Rarity | Effect |
|---|---|---|---|
| `PK_LOADED_DIE` | Loaded Die | Common | +1 to all Pip results (max 6) |
| `PK_SECOND_THOUGHT` | Second Thought | Common | +1 Reroll Charge per stage |
| `PK_MOMENTUM_DIE` | Momentum | Rare | Every 4th roll is automatically a `Star` |
| `PK_FORTUNES_FAVOUR` | Fortune's Favour | Rare | `Fortune` faces also grant +1 Reroll Charge |
| `PK_CHAINBREAKER` | Chainbreaker | Rare | `Chain` faces chain up to 5 times |
| `PK_TWIN_FATES` | Twin Fates | Epic | `Star` faces resolve the landed tile twice |
| `PK_WEIGHTED_FATE` | Weighted Fate | Epic | Convert your lowest Pip face into a `Surge` |
| `PK_DICELORD_GIFT` | The Dicelord's Gift | Legendary | ⚠️ **Redesign pending (O35, `16` B4).** Its fork-choice clause is superseded — fork choice is always free (`03` §1.1, ruled in `16` A7). Placeholder effect until re-ruled: your `6` face becomes a `Star`. Do not build content or balance against this row. |

🔒 All 12 Dice & Board perks are **ineligible in PvP** (`11` §3). They are pure PvE value.

---

## 6. Presentation

- The die is a chunky 3D-look 2D sprite rendered with a squash-and-stretch tumble, **0.8 s**, landing with a bounce and a small dust puff.
- Upgraded faces are visually distinct at a glance: `Star` is gold with rays, `Surge` cyan with a lightning glyph, `Fortune` green with a coin, `Void` a black hole with a purple rim, `Chain` orange links.
- The result is echoed as a large floating number above the hero token before movement begins.
- Haptics: light tap on roll start, medium on land, heavy on `Star`/`Fortune`.
- Tapping and holding the die at any time shows the current 6 faces in a fan layout.

📐 TUNABLE: animation timings must be reducible via the "Fast Mode" setting to 0.25 s.

🔒 **No die skins in v1.** All cosmetic rewards were cut (decision D14), so there is exactly one die design and eleven face artworks (`15` §E16).

⚠️ **Flagged for later:** die skins remain the cheapest and most thematic cosmetic this game could ever add — roughly 11 assets per skin and no system changes anywhere else. If the ladder or mastery tracks need a visual trophy after launch, this is the re-entry point. See `16_DECISION_LOG.md` R3.

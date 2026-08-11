# 03 — Board Generation & Tile Types

🔒 LOCKED: Procedural boards inside authored chapters. The chapter authors the *rules and content*; the generator authors the *layout*.

---

## 1. Board topology

A board is a **directed acyclic graph** of nodes, but visually and mechanically it reads as a mostly-linear track with occasional two-way forks that rejoin.

```
Stage 1 (12 nodes)          Stage 2 (14 nodes)              Stage 3 (16 nodes)      Boss
●─●─●─┬─●─●─●─┬─●─●─●─●─▶ ●─●─┬─●─●─●─●─┬─●─●─●─●─●─▶ ●─●─●─┬─●─●─●─●─┬─●─●─●─●─●─▶ ★
      │       │                │         │                  │         │
      └─●─●───┘                └─●─●─●───┘                  └─●─●─●───┘
        (fork branch)
```

### Rules
- Each stage is a linear spine with **1–2 forks**. A fork branch is 2–4 nodes long and rejoins the spine.
- Forks are the board's main *decision*: each branch is labelled with a preview icon set (e.g. "⚔⚔💰" vs "🎲🛡❓") so the player chooses a risk profile, not a coin flip.
- Movement is always forward. There is no backtracking.
- If a die roll would move the player past the last node of a stage, the player stops on the last node and the Stage Gate fires. (No overshoot waste — overshoot punishment feels bad on a die-driven board.)
- The boss node is always reached exactly; the final roll before it is clamped.

📐 TUNABLE: nodes per stage, fork count, fork length.

---

## 2. Tile types (14)

| ID | Name | Icon | Frequency band | Effect |
|---|---|---|---|---|
| `TILE_ENEMY` | Enemy | ⚔ | Very high | Auto-battle vs a standard enemy. Win → Gold, Legend XP, **perk draft**. |
| `TILE_ELITE` | Elite | ☠ | Low | Auto-battle vs an Elite (2.2× power, unique modifier). Win → Gold, XP, **guaranteed gear drop**, perk draft from an upgraded pool. |
| `TILE_BOSS` | Boss | ★ | Fixed (1/run) | Chapter boss. Multi-phase. Win → run victory. |
| `TILE_SHRINE` | Shrine (Buff) | ✨ | Medium | Choose 1 of 2 permanent-for-this-run stat buffs (e.g. +12% ATK, or +18% Max HP and heal that amount). |
| `TILE_CURSE` | Cursed Ground | 💀 | Medium | Forced debuff, but pays. E.g. "−15% DEF for the rest of the run, +300 Gold". Some curses can be cleansed at a Shrine or by an ad (`AD_SKIP_CURSE`). |
| `TILE_TREASURE` | Treasure | 🎁 | Medium | Instant meta-currency: Crowns, Enhance Stones, Merge Dust. Ad-doubleable (`AD_DOUBLE_CHEST`). |
| `TILE_SHOP` | Shop | 🏪 | Guaranteed ≥1 per stage | 4 offers for Gold: a perk, a consumable, a stat buff, a heal. One refresh free, more via ad. |
| `TILE_CAMPFIRE` | Campfire | 🔥 | Guaranteed 1 before boss | Choose: heal 40% Max HP · upgrade one owned perk to its next tier · gain 2 Reroll Charges. |
| `TILE_MINIGAME` | Minigame | 🎯 | Medium | One of 4 minigames (§6). Skill/luck for a reward. |
| `TILE_EVENT` | Event | ❓ | Medium | A text choice card with 2–3 options and uncertain outcomes. |
| `TILE_PORTAL` | Portal | 🌀 | Low | Jump forward 3–6 nodes, skipping their content. Good when low on HP, bad for greed. |
| `TILE_CACHE` | Beast Cache | 🐾 | Low | Beast Feed or (rarely) a Pet Egg. |
| `TILE_DICE_FORGE` | Dice Forge | 🎲 | Low | Temporarily upgrade one die face for the rest of the run (e.g. turn a `1` into a `4`, or into a `★`). |
| `TILE_EMPTY` | Waypoint | ・ | Filler | Nothing. Used as spacing so the board breathes and rolls feel varied. |

### 2.1 Frequency bands

Weights are per-chapter data, not hardcoded. Reference distribution for Chapter 1, Stage 1:

```json
{
  "TILE_ENEMY": 34, "TILE_EMPTY": 12, "TILE_SHRINE": 9, "TILE_TREASURE": 8,
  "TILE_EVENT": 8, "TILE_MINIGAME": 7, "TILE_CURSE": 6, "TILE_SHOP": 5,
  "TILE_ELITE": 4, "TILE_CACHE": 3, "TILE_PORTAL": 2, "TILE_DICE_FORGE": 2
}
```

Later chapters shift weight from `TILE_EMPTY` toward `TILE_ELITE` and `TILE_CURSE`.

---

## 3. Generation algorithm

```
GenerateBoard(chapter, tier, seed):
    rng = SeededRng(seed, stream="board")
    board = new Board()

    for stage in [1,2,3]:
        spineLength = chapter.stageLengths[stage]        // 12 / 14 / 16
        nodes = new List<Node>(spineLength)

        // --- Step 1: place mandatory tiles ---
        place TILE_SHOP     at a random index in [3 .. spineLength-3]
        if stage == 3:
            place TILE_CAMPFIRE at spineLength-2          // always just before boss
        place TILE_ELITE    count = chapter.eliteCount[stage]   // 1 in S1, 1-2 in S2, 2 in S3
            constraint: not adjacent to another elite, not in first 2 nodes

        // --- Step 2: fill remaining with weighted draw ---
        for each empty index:
            t = WeightedDraw(chapter.tileWeights[stage], rng)
            apply constraints (Step 3); redraw up to 8 times, else fall back to TILE_ENEMY

        // --- Step 3: constraints ---
        //  C1: no 3 identical non-ENEMY tiles in a row
        //  C2: no 4 consecutive TILE_ENEMY (insert TILE_EMPTY or TILE_SHRINE)
        //  C3: at least 1 healing opportunity (SHRINE/CAMPFIRE/SHOP) per 8 nodes
        //  C4: TILE_PORTAL never in the last 4 nodes of a stage
        //  C5: TILE_CURSE never immediately before TILE_ELITE or TILE_BOSS
        //  C6: first node of stage 1 is always TILE_ENEMY (teach combat immediately)
        //  C7: every run contains >= 2 TILE_TREASURE and >= 1 TILE_CACHE across all
        //      three stages. If the weighted draw did not produce them, inject them
        //      by replacing TILE_EMPTY tiles (or, failing that, TILE_ENEMY tiles) at
        //      the latest available indices. See 24_LUCK_PROTECTION.md §4.10 B1.

        // --- Step 4: forks ---
        forkCount = rng.Range(1, 2)
        for each fork:
            pick a spine index in [4 .. spineLength-4] not occupied by SHOP/CAMPFIRE
            branchLen = rng.Range(2, 4)
            generate branch nodes with a *biased* weight table:
                one branch biased to combat+reward ("Perilous")
                the other biased to utility+safety ("Sheltered")
            rejoin at spineIndex + branchLen

        board.AddStage(nodes, forks)

    board.AddBossNode(chapter.bossId)
    return board
```

### 3.1 Fork previews

Each branch shows up to 3 icons summarising its contents, plus a one-word label:

| Label | Bias |
|---|---|
| **Perilous** | +Elite, +Enemy, +Treasure, −Shrine |
| **Sheltered** | +Shrine, +Campfire, +Shop, −Enemy |
| **Arcane** | +Dice Forge, +Event, +Minigame |
| **Feral** | +Cache, +Curse, +Elite |

The preview must be honest — it lists real contents. Deception here would poison the one real navigation decision the player makes.

---

## 4. Chapter definitions (8)

Each chapter is a data file. Fields: `id`, `displayName`, `biomeArtSet`, `powerTarget`, `stageLengths`, `tileWeights[3]`, `eliteCount[3]`, `enemyPool`, `elitePool`, `bossId`, `lootTable`, `musicId`, `paletteId`, `unlockCondition`.

| # | Chapter | Biome | Theme keywords (for art) | Boss | Signature mechanic |
|---|---|---|---|---|---|
| 1 | **Greenwood Vale** | Sunlit forest | mossy stones, mushrooms, fireflies, warm greens | **Thornmaw**, a giant carnivorous flower | Tutorial-friendly, generous shrines |
| 2 | **Ashen Mire** | Poison swamp | bogs, purple fog, twisted roots, bubbling tar | **Gulgrot**, a bloated toad shaman | Poison DoT enemies; healing matters |
| 3 | **Sunken Crypt** | Undead catacomb | bone arches, candles, cracked sarcophagi, teal light | **Ossuary King**, a crowned skeleton | Enemies resurrect once at 20% HP (§4.1) |
| 4 | **Emberpeak** | Volcano | obsidian, lava rivers, ember particles, orange/black | **Cindermaw**, a magma drake | Burning tiles: landing on a marked tile costs HP (§4.1) |
| 5 | **Frostbound Reach** | Glacier | ice spires, aurora, pale blues, snow drifts | **Rimehold**, an ice golem | Freeze: attack speed periodically halved |
| 6 | **Clockwork Vaults** | Brass machine dungeon | gears, pipes, steam, copper/teal | **Cogitator Prime**, a spider automaton | Clockwork Pressure: enemy power grows with each roll taken (§4.1) |
| 7 | **Bloom of Decay** | Fungal overgrowth | bioluminescent spores, rot pinks, giant caps | **Sporequeen Vell** | Spore clouds add a stacking debuff |
| 8 | **Astral Spire** | Celestial tower | starfields, floating platforms, violet/gold | **The Dicelord**, a masked cosmic figure | Random reality shifts: one die face is scrambled each stage |

### 4.1 Chapter signature mechanics — rulings 🔒

Three signatures were named but underspecified; these rulings define them. All are expressible in the effect DSL (`18`) and live in the chapter data file.

| Chapter | Ruling |
|---|---|
| **3 — Sunken Crypt** | Chapter-pool normal enemies **and Elites** carry `ON_LETHAL (once) → REVIVE at 20% Max HP`. Active DoTs and debuffs **persist** through the revive (DoT builds are the natural counter — a chapter build identity). `SWARM` units revive individually. The boss is excluded — Ossuary King has his own Rise Again (`17` §4). |
| **4 — Emberpeak** | **Burning tiles.** The generator marks ~20% 📐 of non-mandatory tiles as *Burning*, visibly flagged on the board. Landing on one costs **4% Max HP** 📐 before the tile resolves. A board-layer hazard the player can route around at forks and with `Star` faces — it makes board-control tools matter. `MNT_GLIDEWING`-style portal play and high preview range are the soft counters. Never on `TILE_CAMPFIRE`, the boss node, or the first 2 tiles of Stage 1. |
| **6 — Clockwork Vaults** | **Clockwork Pressure.** Each die roll taken in the **current stage** adds **+5%** 📐 enemy power to subsequent battles, capped at **+50%** 📐, resetting at each Stage Gate. A board-level clock: efficient routing (portals, high rolls, `Chain`) is rewarded, dawdling is taxed. Displayed as a small gear counter in the board HUD. |

Chapters 2, 5 and 7 need no ruling — their signatures (poison enemies, freeze, spore stacks) are ordinary combat effects already covered by §5 of `05`. Chapter 8's die scramble is specified via `MODIFY_DIE_FACE` (`18` §2.5).

🔒 **Narrative scope: flavour text only.** Chapter names, boss names, one-line tile and event flavour, item descriptions. **No plot, no cutscenes, no recurring characters, no explanation of why the hero climbs.** The genre does not need it, it keeps localisation cheap, and it avoids setting a story expectation the rest of the game does not meet. The voice throughout is dry, terse and slightly wry — see the 30 event cards in `19_CONTENT_TABLES.md` Part A for the tone reference.

---

## 5. Events (`TILE_EVENT`)

An Event presents a card: title, one-line fiction, and 2–3 options. Options can have requirements and uncertain outcomes.

Format:

```json
{
  "id": "EVT_WELL",
  "title": "The Wishing Well",
  "body": "Coins glitter under black water.",
  "options": [
    { "label": "Toss in 100 Gold", "cost": {"gold": 100},
      "outcomes": [ {"w": 60, "effect": "GAIN_PERK_RANDOM"},
                    {"w": 40, "effect": "GAIN_CROWNS", "amount": 40} ] },
    { "label": "Reach in", "cost": {"hpPct": 10},
      "outcomes": [ {"w": 70, "effect": "GAIN_ENHANCE_STONE", "amount": 3},
                    {"w": 30, "effect": "APPLY_CURSE", "curseId": "CUR_SLIPPERY"} ] },
    { "label": "Walk away", "outcomes": [ {"w": 100, "effect": "NONE"} ] }
  ]
}
```

**v1 target: 30 events.** ✅ **All 30 are authored in `19_CONTENT_TABLES.md` Part A**, grouped by the chapter band in which they appear. Their outcome weights and value scalars still need to pass through the economy simulator before being treated as final.

---

## 6. Minigames (`TILE_MINIGAME`) — 4 in v1

All minigames are ≤ 15 seconds, one-thumb, and *cannot* fail catastrophically — worst case is a small reward.

| ID | Name | Interaction | Reward scale |
|---|---|---|---|
| `MG_CHEST_PICK` | **Three Chests** | Pick 1 of 3 shuffled chests. One is gold-tier. | Low variance |
| `MG_TIMING_BAR` | **Strike the Anvil** | A marker sweeps a bar; tap in the shrinking green zone. 3 attempts, each success upgrades the reward tier. | Skill-scaled |
| `MG_DICE_DUEL` | **Dice Duel** | Best-of-3 die rolls vs. an NPC gambler, using your actual upgraded die faces. | Rewards die-face investment |
| `MG_MEMORY_RUNE` | **Rune Recall** | 4-symbol Simon-style sequence, 2 rounds. | Skill-scaled |

Failed minigames offer a single ad-retry (`AD_RETRY_MINIGAME`, 1/run).

### 6.1 Reward tables 🔒

Values below are **Chapter 1 base values**, multiplied by `(1 + 0.35 × (chapter − 1))` 📐 — the same scaling shape as the ad bundles (`12` §5). All 📐 TUNABLE, in `data/tuning/currencies.json`; the simulator validates them.

| Minigame | Outcome | Reward |
|---|---|---|
| `MG_CHEST_PICK` | Bronze chest | 150 Gold |
| | Silver chest | 300 Gold + 20 Crowns |
| | Gold chest | 500 Gold + 60 Crowns + 15 Beast Feed |
| `MG_TIMING_BAR` | 0 hits | 100 Gold |
| | 1 hit | 250 Gold |
| | 2 hits | 400 Gold + 30 Crowns |
| | 3 hits | 600 Gold + 80 Crowns + 5 Enhance Stones |
| `MG_DICE_DUEL` | Loss | 150 Gold |
| | Win 2–1 | 400 Gold + 40 Crowns |
| | Win 2–0 | 550 Gold + 50 Crowns + 1 Reroll Charge |
| `MG_MEMORY_RUNE` | Fail round 1 | 100 Gold |
| | Clear round 1 only | 300 Gold + 25 Crowns |
| | Clear both rounds | 550 Gold + 70 Crowns + 20 Beast Feed |

`MG_CHEST_PICK`'s gold-tier guarantee is `24` §4.9. Rewards that grant gear route through `LuckService` as class `CHEST_STANDARD` (`24` §3).

### 6.2 Server authority for skill minigames 🔒

`MG_CHEST_PICK` and `MG_DICE_DUEL` are server-rolled like everything else. `MG_TIMING_BAR` and `MG_MEMORY_RUNE` are **genuine skill inputs, and their outcomes are client-asserted** — a deliberate, documented exception to `14` §2.1:

- `MINIGAME_SUBMIT` carries the claimed result; the server validates **legality only** (a valid outcome tier, exactly one submission per tile, rate limits).
- Accepted because the worst case is a player granting themselves a small, capped, run-local reward — and the alternative (server-scripted "skill") would be dishonest.
- Recorded in `14` §9's anti-cheat table so nobody later mistakes it for an oversight.

📐 TUNABLE: reward tables per minigame per chapter.

---

## 7. Shop tile detail

The shop offers exactly **4 slots**, drawn from separate pools so the offer is always varied:

| Slot | Pool |
|---|---|
| 1 | A **Perk** (rarity-weighted, priced by rarity) |
| 2 | A **Consumable** (Health Draught, Reroll Token, Draft Token, Escape Rope) |
| 3 | A **Run Buff** (flat +ATK / +HP / +Crit for the rest of the run) |
| 4 | A **Heal** (restore 35% Max HP), always available, price scales with stage |

Pricing:
```
Price = BasePrice(itemType, rarity) * (1 + 0.25 * stageIndex) * chapterPriceScalar
```

Refresh: **1 free refresh per shop visit**, then `AD_SHOP_REFRESH` (2/run), then unavailable.

Gold is run-local, so the design intent is that a player should end a run with near-zero Gold. If telemetry shows median leftover Gold > 20% of Gold earned, prices are too high.

---

## 8. Board presentation notes

- The board scrolls vertically; the hero token sits at roughly 40% screen height with the upcoming path visible above.
- Tiles are drawn as flat, chunky, high-contrast pucks with a large icon. Readability at 48 dp is mandatory.
- Already-resolved tiles dim to 55% opacity and lose their icon glow.
- The next 6 tiles are always visible without scrolling; the player may free-scroll ahead to plan and a "recenter" button returns to the token.
- Fork branches are drawn side by side with a clear join, never as ambiguous crossing lines.

See `15_ART_DIRECTION_AND_ASSET_MANIFEST.md` §E8 (tile icons) and §E9 (board paths and decor) for the tile art specification.

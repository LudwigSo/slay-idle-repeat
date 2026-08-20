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
- Movement resolution — the virtual start, junction pauses, Portal jumps and the linear node index — is specified in §1.1, which is the single authority; `02` §3 and `04` defer to it.
- If a die roll would move the player past the last node of a stage, the player stops on the last node and the Stage Gate fires. (No overshoot waste — overshoot punishment feels bad on a die-driven board.)
- The boss node is always reached exactly; the final roll before it is clamped.

📐 TUNABLE: nodes per stage, fork count, fork length.

### 1.1 Movement resolution 🔒 (ruled in `16` A7)

**Start position.** The hero begins every run at a virtual **trailhead** one step before node 0 (position −1). The trailhead is not a tile: nothing resolves there, and the token is drawn at the head of the track. A first roll of `1` therefore lands on node 0.

**Stepwise traversal.** All forward movement — the number a roll came up, and Portal jumps — traverses the graph **one edge at a time**. Passed-over nodes never resolve; only the landing node resolves.

**Junction pause.** A junction is a node with two outgoing edges (the spine continuation and a branch entry). Whenever movement must **leave** a junction — whether the move started there or reached it mid-move — movement pauses and the run waits for `CHOOSE_FORK` (`14` §2.3), with the branch previews of §3.1 shown. The remaining movement then continues along the chosen edge. Landing exactly on a junction with zero movement left does not prompt; the choice happens when the next movement leaves it. Fork choice is **always free and always explicit** — there is no "landed segment" condition, and no perk, face or item is required to choose a branch.

**Stage-end clamp.** A move that would carry past the last node of a stage stops **on** that node; the tile resolves, then the Stage Gate fires (`02` §1.2). The boss node is always reached **exactly**: any roll taken from stage 3's last node moves exactly one step onto the boss node — this is the "final roll before it is clamped" rule, made precise.

⚠️ **Chained rolls are gone.** A `Chain` face used to move 2, resolve its landing tile in full, and then fire another roll automatically — up to a cap, with the stage-gate clamp and the boss node ending the sequence. The die has no face kinds (`04` §5), so **no roll can ever ask to roll again**: one `ROLL_DICE` is one movement and one landing, always.

**Portal (`TILE_PORTAL`).** Resolving a Portal draws its jump distance **uniformly from 3–6**, seeded from the run's `board` stream (`14` §8.1) — never chosen by the player, never rolled client-side. The jump is stepwise traversal like any move: junctions inside the jump still pause for `CHOOSE_FORK`, and passed nodes do not resolve. Clamps: the jump obeys the **stage-end clamp** above, and in stage 3 it additionally never carries past the guaranteed pre-boss campfire (§3 step 1) — a draw that would pass it lands **on** the campfire instead. The generous reading is deliberate: a portal that skips the one guaranteed heal before the boss would be a trap wearing a gift's colours. Consequence: a Portal can never reach the boss node — the boss is entered only by a die move, under the reached-exactly rule. 📐 TUNABLE: the 3–6 range.

**Linear node index.** `EnemyPower(i)` (`02` §4.3) takes a per-node index `i`, fixed at generation. Spine nodes are numbered `0..41` in walk order across the three stages (12 + 14 + 16); the boss node is `42`. A **branch node's index equals the index of the spine node at the same forward distance from the junction**: the k-th node of a branch leaving the spine at node `j` has `i = i(j) + k`. Branches rejoin at `j + branchLen` (§3 step 4), so the parallel spine node always exists. Branch and spine therefore pay identical `EnemyPower` at equal forward progress — a fork is a risk-profile choice, never a power discount. The rule is structural and survives any change to board lengths.

---

## 2. Tile types (14)

| ID | Name | Icon | Frequency band | Effect |
|---|---|---|---|---|
| `TILE_ENEMY` | Enemy | ⚔ | Very high | Auto-battle vs a standard enemy. Win → Gold, Legend XP, **perk draft**. |
| `TILE_ELITE` | Elite | ☠ | Low | Auto-battle vs an Elite (2.2× power, unique modifier). Win → Gold, XP, **guaranteed gear drop**, perk draft from an upgraded pool. |
| `TILE_BOSS` | Boss | ★ | Fixed (1/run) | Chapter boss. Multi-phase. Win → run victory. |
| `TILE_SHRINE` | Shrine (Buff) | ✨ | Medium | Choose 1 of 2 permanent-for-this-run buffs from the authored pool in §7a.5 — the choice arrives as `SHRINE_CHOOSE` (`14` §2.3, `16` D38). When the player carries a cleansable curse, a **Cleanse** offer always replaces the second option (§7a.5). |
| `TILE_CURSE` | Cursed Ground | 💀 | Medium | Forced debuff, but pays. E.g. "−15% DEF for the rest of the run, +300 Gold". Some curses can be cleansed at a Shrine or by an ad (`AD_SKIP_CURSE`). ⚠️ `16` D40: six of the twelve are applied as stat moves and one as a board rule; the other five are carried by the run — cleansable, nameable — with the debuff not applied and the missing mechanism written down per curse. |
| `TILE_TREASURE` | Treasure | 🎁 | Medium | Meta-currency: Crowns, Enhance Stones, Merge Dust — payout table §7a.3. Revealed on landing, banked at run end like all meta rewards (§7a). Ad-doubleable (`AD_DOUBLE_CHEST`). |
| `TILE_SHOP` | Shop | 🏪 | Guaranteed ≥1 per stage | 4 offers for Gold: a perk, a consumable, a stat buff, a heal. One refresh free, more via ad. The visit opens on `RESOLVE_TILE` and closes on `SHOP_LEAVE` (`16` D38) — it is the one tile a player stands at for several commands by choice. |
| `TILE_CAMPFIRE` | Campfire | 🔥 | Guaranteed 1 before boss | Choose: heal 40% Max HP · upgrade one owned perk to its next tier. ⚠️ **Two options, not three** — the third was *gain 2 Reroll Charges*, removed with the reroll (`04` §5). A two-way campfire is a thinner decision than the design intends; the empty seat is owed a replacement. |
| `TILE_MINIGAME` | Minigame | 🎯 | Medium | One of 4 minigames (§6). Skill/luck for a reward. |
| `TILE_EVENT` | Event | ❓ | Medium | A text choice card with 2–3 options and uncertain outcomes. |
| `TILE_PORTAL` | Portal | 🌀 | Low | Jump forward a seeded 3–6 node draw (§1.1), skipping the passed content. Good when low on HP, bad for greed. |
| `TILE_CACHE` | Beast Cache | 🐾 | Low | Beast Feed, or (6% 📐) a Pet Egg — payout table §7a.4. |
| `TILE_DICE_FORGE` | Dice Forge | 🎲 | Low | 🔴 **Does nothing, and is kept for a repurposing** (`04` §5.1). It upgraded one die face for the rest of the run through `DICE_FORGE_CHOOSE`; the die has no faces and that command is gone, so landing on one **resolves in place and grants nothing**. It still occupies one of the `Arcane` fork's three outcomes (§3.1), so the hole is in the board's reward texture rather than nowhere. |
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

🔒 **Authored boards** (ruled in `16` A7): when the active content package supplies a fixed layout (`board.layout` — a node list in walk order, with fork structure if any), `GenerateBoard` is **bypassed** and the layout is used verbatim. The generator's mandatory-tile placement and constraints C1–C7 do **not** apply to an authored layout — the author is responsible for it, and the content schema validator checks it structurally instead (valid tile IDs, exactly one boss/mini-boss node in final position, walkability of any forced roll sequence shipped with it). The FTUE (`ftue.json`, `19` Part D) is the first authored board; Resource Dungeons continue to use `GenerateBoard` with a dungeon profile (`25` §3).

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
        //  C6: first node of stage 1 is always TILE_ENEMY. The hero starts at the
        //      trailhead BEFORE node 0 (§1.1), so the earliest possible landing —
        //      a first roll of 1 — is always a fight, and the run's opening beat
        //      is combat whenever the die allows it. The FTUE forces its first
        //      roll to 1 for exactly this reason (19 Part D3).
        //  C7: every run contains >= 2 TILE_TREASURE and >= 1 TILE_CACHE across all
        //      three stages. If the weighted draw did not produce them, inject them
        //      by replacing TILE_EMPTY tiles (or, failing that, TILE_ENEMY tiles) at
        //      the latest available indices. See 24_LUCK_PROTECTION.md §4.10 B1.

        // --- Step 4: forks ---
        forkCount = rng.Range(1, 2)
        for each fork:
            pick a spine index in [4 .. spineLength-4] not occupied by SHOP/CAMPFIRE
            branchLen = rng.Range(2, min(4, spineLength - 1 - spineIndex))
            // clamped so the rejoin node spineIndex + branchLen always exists
            // on the spine (≤ spineLength-1) — required by the §1.1 branch-index rule
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

Each chapter is a data file. Fields: `id`, `displayName`, `biomeArtSet`, `powerTarget`, `stageLengths`, `tileWeights[3]`, `eliteCount[3]`, `enemyPool`, `elitePool`, `bossId`, `lootTable`, `musicId`, `paletteId`, `unlockCondition`. `enemyPool` / `elitePool` composition for all 8 chapters is authored in `05` §6.4 and §6.2 (ruled in `16` A7).

| # | Chapter | Biome | Theme keywords (for art) | Boss | Signature mechanic |
|---|---|---|---|---|---|
| 1 | **Greenwood Vale** | Sunlit forest | mossy stones, mushrooms, fireflies, warm greens | **Thornmaw**, a giant carnivorous flower | Tutorial-friendly, generous shrines |
| 2 | **Ashen Mire** | Poison swamp | bogs, purple fog, twisted roots, bubbling tar | **Gulgrot**, a bloated toad shaman | Poison DoT enemies; healing matters |
| 3 | **Sunken Crypt** | Undead catacomb | bone arches, candles, cracked sarcophagi, teal light | **Ossuary King**, a crowned skeleton | Enemies resurrect once at 20% HP (§4.1) |
| 4 | **Emberpeak** | Volcano | obsidian, lava rivers, ember particles, orange/black | **Cindermaw**, a magma drake | Burning tiles: landing on a marked tile costs HP (§4.1) |
| 5 | **Frostbound Reach** | Glacier | ice spires, aurora, pale blues, snow drifts | **Rimehold**, an ice golem | Freeze: attack speed periodically halved |
| 6 | **Clockwork Vaults** | Brass machine dungeon | gears, pipes, steam, copper/teal | **Cogitator Prime**, a spider automaton | Clockwork Pressure: enemy power grows with each roll taken (§4.1) |
| 7 | **Bloom of Decay** | Fungal overgrowth | bioluminescent spores, rot pinks, giant caps | **Sporequeen Vell** | Spore clouds add a stacking debuff |
| 8 | **Astral Spire** | Celestial tower | starfields, floating platforms, violet/gold | **The Dicelord**, a masked cosmic figure | ⚠️ *Random reality shifts: one die face is scrambled each stage* — **unbuildable and unreplaced**, the die has no faces to scramble (`04` §5). Chapter 8 currently has no chapter modifier. |

### 4.1 Chapter signature mechanics — rulings 🔒

Three signatures were named but underspecified; these rulings define them. All are expressible in the effect DSL (`18`) and live in the chapter data file.

| Chapter | Ruling |
|---|---|
| **3 — Sunken Crypt** | Chapter-pool normal enemies **and Elites** carry `ON_LETHAL (once) → REVIVE at 20% Max HP`. Active DoTs and debuffs **persist** through the revive (DoT builds are the natural counter — a chapter build identity). `SWARM` units revive individually. The boss is excluded — Ossuary King has his own Rise Again (`17` §4). |
| **4 — Emberpeak** | **Burning tiles.** The generator marks ~20% 📐 of non-mandatory tiles as *Burning*, visibly flagged on the board. Landing on one costs **4% Max HP** 📐 before the tile resolves. A board-layer hazard the player can route around at forks — ⚠️ routing around it with a `Star` face's chosen movement is no longer possible (`04` §5), so the only counter left is the fork. `MNT_GLIDEWING`-style portal play and high preview range are the soft counters. Never on `TILE_CAMPFIRE`, the boss node, or the first 2 tiles of Stage 1. |
| **6 — Clockwork Vaults** | **Clockwork Pressure.** Each die roll taken in the **current stage** adds **+5%** 📐 enemy power to subsequent battles, capped at **+50%** 📐, resetting at each Stage Gate. A board-level clock: efficient routing (portals, high rolls) is rewarded, dawdling is taxed. ⚠️ `Chain` is no longer one of its tools (`04` §5). Displayed as a small gear counter in the board HUD. |

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
| `MG_DICE_DUEL` | **Dice Duel** | Best-of-3 die rolls vs. an NPC gambler. | ⚠️ It rewarded die-face investment; there is none to reward (`04` §5), so it is now a coin-flip minigame with nothing behind it. Owed a rethink. |
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
| | Win 2–0 | 550 Gold + 50 Crowns ⚠️ (the Reroll Charge is removed — `04` §5) |
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
| 2 | A **Consumable** (§7.1: Health Draught, Draft Token, Escape Rope) |
| 3 | A **Run Buff** (flat +ATK / +HP / +Crit for the rest of the run) |
| 4 | A **Heal** (restore 35% Max HP), always available, price scales with stage |

Pricing 🔒 (ruled in `16` A7 — all values 📐, in `data/tuning/currencies.json`):

```
Price = BasePrice(itemType, rarity) * (1 + 0.25 * stageIndex) * chapterPriceScalar

stageIndex          = 0 / 1 / 2 for Stage 1 / 2 / 3   (stage 1 pays base price)
chapterPriceScalar  = 1.55^(c-1)                       // the same growth as Gold income (§7a.1),
                                                       // so affordability is chapter-invariant
```

| Chapter | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| `chapterPriceScalar` 📐 | 1.00 | 1.55 | 2.40 | 3.72 | 5.77 | 8.95 | 13.86 | 21.50 |

**`BasePrice(itemType, rarity)`** 📐 — all four slot types:

| Slot | Item | BasePrice |
|---|---|---|
| 1 — Perk | Common / Rare / Epic / Legendary | 180 / 320 / 560 / 950 |
| 2 — Consumable | Health Draught (heals 30% Max HP, board-use — `16` A7) | 140 |
| | Draft Token (+1 free perk-draft reroll on purchase — §7.1) | 160 |
| | Escape Rope (skips the next tile) | 100 |
| 3 — Run Buff | see the magnitude table below | 300 / 300 / 280 |
| 4 — Heal | Restore 35% Max HP (always offered) | 150 |

**Run Buffs** 📐 — flat, permanent for this run, applied as `FlatAdd` in the `05` §1.1 aggregation. Flat amounts double per chapter so they track the ×2 chapter power curve; the Crit buff feeds a capped stat and does not scale:

| Run Buff | Magnitude (chapter c) | BasePrice |
|---|---|---|
| Whetstone | +`12 × 2^(c-1)` ATK | 300 |
| Heartroot Tonic | +`90 × 2^(c-1)` Max HP | 300 |
| Hawk's Eye | +4% Crit Chance (flat points, chapter-invariant) | 280 |

**Tier rule** 🔒: Gold income (§7a.1) and shop prices are both **tier-invariant** — the `02` §4.2 Reward × multiplier (Heroic ×2.5, Mythic ×6.0) applies to *banked meta rewards* (Crowns, Stones, Dust, Feed, Soul Shards, XP per `02` §5.1a), never to run-local Gold or to prices. A Heroic shop is neither cheaper nor richer, only the run around it pays more.

The FTUE's beat-5 tutorial shop does **not** use this formula — it has one authored price row in `ftue.json` (`19` Part D).

Refresh: **1 free refresh per shop visit**, then `AD_SHOP_REFRESH` (2/run), then unavailable.

Gold is run-local, so the design intent is that a player should end a run with near-zero Gold. If telemetry shows median leftover Gold > 20% of Gold earned, prices are too high.

### 7.1 Consumables 🔒 (ruled in `16` A7)

Consumables are **run-scoped and board-use only**. They are sold at Shop tiles (slot 2) and occasionally granted by events (`19` Part A — `EVT_SPRING`). Nothing survives run end, and nothing is ever usable during combat — D3's "zero inputs during a fight" stands untouched. Use is the `USE_CONSUMABLE` command (payload in `14` §2.3), legal **only in the `AWAIT_ROLL` state** — never while moving, never during a prompt, never in battle.

| ID | Name | On purchase | Effect |
|---|---|---|---|
| `CON_HEALTH_DRAUGHT` | Health Draught | Held | Use on the board: heal **30% Max HP** 📐. Disabled at full HP — a draught can never be wasted by a mis-tap. |
| `CON_DRAFT_TOKEN` | Draft Token | **Instant: +1 free perk-draft reroll** (`06` §1) | Never held. Greys out when free draft rerolls are at their cap (3). |
| `CON_ESCAPE_ROPE` | Escape Rope | Held | Use on the board: **arms** the rope. The next tile the hero lands on is **skipped**: it is marked resolved, its content does not run, it pays nothing, and landing-triggered board hazards (Ch. 4 Burning tiles, §4.1) do not fire. The rope is consumed when it fires. |

Rules:

- **Held cap: 4** 📐, counted across all held consumables (Draughts + Ropes). Only Draughts and Ropes are ever actually held — the two tokens convert to their charge at the till. A purchase or event grant that would exceed the cap is unavailable (the shop slot greys out); an event grant clamps to what fits (`EVT_SPRING`'s ×2 grants one if only one fits).
- Only **one** rope may be armed at a time. An armed rope persists across rolls until it fires.
- The rope never skips `TILE_BOSS`: if the next landing is the boss node, the boss resolves normally and the rope simply never fires. The **Stage Gate is positional, not tile content**: landing on a stage's last node with a rope armed skips that tile's content, but the gate (heal, refresh, checkpoint — `02` §1.2) still fires.
- ⚠️ The two clauses that used to sit here were about a `Fortune` face's double-pay on a skipped tile and a `Chain` hop landing while a rope is armed. Both faces are gone (`04` §5), so a rope now has exactly one interaction: it skips the tile the next roll lands on.
- Prices come from the consumable rows of `BasePrice(itemType, rarity)` in `currencies.json` through the §7 price formula.
- The HUD home is the S05 consumable pouch (`13` §3). Held consumables and the armed-rope flag are run state (`30` §4).

📐 TUNABLE: the heal percent, the held cap.

---

## 7a. In-run income tables 🔒 (ruled in `16` A7)

The single source of truth for every in-run payout. All values 📐 TUNABLE, in `data/tuning/currencies.json`; `02` §5.1 and `10` §4 point here. Two chapter scalars, both formula-shaped like `BaseXp(c) = 25 × 1.55^(c-1)` (`02` §5.1a):

```
G(c) = 1.55^(c-1)     // Gold — matches XP and shop-price growth exactly
M(c) = 1.35^(c-1)     // in-run meta currency (Crowns, Stones, Dust, Feed) — deliberately
                      // slower than the ×2/chapter power curve, so material income never
                      // outruns the merge bottleneck (`10` §4)
```

Rounding: all computed payouts round to the nearest integer. Tier: Gold is tier-invariant (§7 tier rule); meta-currency payouts are banked rewards and take the `02` §4.2 Reward × multiplier and the `02` §5.2 CompletionMultiplier.

### 7a.1 Gold per kill 📐

```
GoldPerKill(c) = 40 × G(c)        // normal enemy
Elite   ×3
Boss    ×10
```

No Gold is paid for run victory — Gold is wiped at run end (`02` §5.1) and never appears in the run-end payout or the `AD_DOUBLE_RUN_REWARDS` doubling. Supplementary Gold comes from minigames (§6.1, which scale on their own locked curve), events, curse pairings and `MNT_PACKMULE`.

Sanity check (Chapter 1): ~15 normals (600) + 4 elites (480) + boss (400) + ~2 minigames (~500) + curse/event drift (~300) ≈ **2,300 Gold per run**, against ~1,500–2,000 of shop spending at §7 prices — landing the "end a run near zero Gold" target in §7.

### 7a.2 Run-completion Crowns 📐

```
RunCrowns(c) = 90 × M(c)          // banked at run end like every meta reward; takes
                                  // CompletionMultiplier (Victory 1.00, Stage-3 death 0.60,
                                  // per `02` §5.2) and Reward ×
```

This is the income the `10` §4 row "Run completion" refers to; it had no defined source before this ruling.

### 7a.3 `TILE_TREASURE` payout table 📐

Each treasure tile rolls one payout profile (seeded, stream `"treasure"`):

| Weight | Profile | Crowns | Enhance Stones | Merge Dust |
|---|---|---|---|---|
| 55 | Coin hoard | `40 × M(c)` | `2 × M(c)` | — |
| 30 | Stone cache | `18 × M(c)` | `6 × M(c)` | — |
| 15 | Dust trove | `15 × M(c)` | — | `10 × M(c)` |

Expected value per tile ≈ `30 × M(c)` Crowns, `3 × M(c)` Stones, `1.5 × M(c)` Dust. Every run contains 2–3 treasure tiles (constraint C7 guarantees ≥ 2). `AD_DOUBLE_CHEST` doubles the rolled profile. Treasure tiles never drop gear in v1 — `24` §3's `DROP_RUN` listing of "`TILE_TREASURE` gear" covers event-granted gear routed through treasure presentation, not this table.

### 7a.4 `TILE_CACHE` payout table 📐

```
CacheFeed(c)   = 25 × M(c)        // Beast Feed
EggChance      = 6%               // base Pet Egg rate, flat across chapters
```

On an egg hit the cache pays **one Pet Egg instead of the Feed** (granted unopened, `24` §4). The 6% base rate is what makes the `EGG_PET` pity arithmetic in `24` §4.4 meaningful: at ~8 runs/day and ≥1 cache per run (C7), caches alone contribute ~0.6 eggs/day — the 30-egg S guarantee is reachable from play, not only from the shop and the daily ad egg.

### 7a.5 Shrine buff pool 📐

A shrine offers **2 distinct options** drawn seeded (stream `"shrine"`, equal weights) from this pool of 10. The same buff may appear again at a later shrine and **stacks additively**. All buffs are permanent for this run and enter the `05` §1.1 aggregation as `PctAdd` (except the pure heal).

| ID | Buff | Magnitude |
|---|---|---|
| `SHR_ATK` | Sharpened Resolve | +12% ATK |
| `SHR_HP` | Oakheart | +18% Max HP, and immediately heal 18% Max HP |
| `SHR_ASPD` | Quickened Pulse | +10% Attack Speed |
| `SHR_CRIT` | Hunter's Omen | +8% Crit Chance |
| `SHR_DEF` | Stoneskin | +20% DEF |
| `SHR_LS` | Red Thirst | +6% Lifesteal |
| `SHR_DR` | Warding Light | +6% Damage Reduction |
| `SHR_THORN` | Bramble Pact | +25% Thorns |
| `SHR_GOLD` | Gilded Tongue | +15% Gold from all sources this run |
| `SHR_HEAL` | Spring of Mercy | Heal 40% Max HP now (no permanent buff) |

**Cleanse rule** 🔒 (replaces the unconditioned "may" in `19` Part E): if the player has **at least one active cleansable curse** when the shrine resolves, option slot 2 is **always** a Cleanse — *remove one cleansable curse of the player's choice; any reward it paid is kept*. Slot 1 remains a pool draw, so the player always chooses between relief and greed. With no active cleansable curse a shrine never offers Cleanse. `MNT_TIDECALLER`'s second use redraws both slots (and re-applies this rule).

---

## 8. Board presentation notes

- The board scrolls vertically; the hero token sits at roughly 40% screen height with the upcoming path visible above.
- Tiles are drawn as flat, chunky, high-contrast pucks with a large icon. Readability at 48 dp is mandatory.
- Already-resolved tiles dim to 55% opacity and lose their icon glow.
- The next 6 tiles are always visible without scrolling; the player may free-scroll ahead to plan and a "recenter" button returns to the token.
- Fork branches are drawn side by side with a clear join, never as ambiguous crossing lines.

See `15_ART_DIRECTION_AND_ASSET_MANIFEST.md` §E8 (tile icons) and §E9 (board paths and decor) for the tile art specification.

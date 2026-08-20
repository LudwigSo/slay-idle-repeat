# 25 — Resource Dungeons

🔒 **Decision D24: three daily Resource Dungeons ship in v1.**

Every material in the game is currently farmed by playing the same chapter board and hoping. That is a luck problem as much as a content problem: a player who needs Enhance Stones has no way to go and get Enhance Stones. Resource Dungeons are the deterministic counterweight to `24_LUCK_PROTECTION.md` — where that document bounds the tail of randomness, this one removes randomness from material income entirely.

---

## 1. Design constraints

These are the rails. A Resource Dungeon that breaks one of them has become a second game.

| # | Constraint |
|---|---|
| **C1** | **Deterministic rewards.** A dungeon clear pays a **fixed table**. No rarity roll, no drop chance, no variance. The only variable is how deep the player clears. |
| **C2** | **Short.** 2–4 minutes. A dungeon is a chore the player is happy to do, not a session. |
| **C3** | **No gear.** Dungeons never drop equipment. The loot chase stays in chapter runs, where the drama is. |
| **C4** | **Not the optimal levelling route.** Legend XP from dungeons is deliberately poor (§4). A player who only runs dungeons must fall behind a player who runs chapters. |
| **C5** | **Costs Energy.** Dungeons compete with runs for the same resource, so a player chooses rather than adds. |
| **C6** | **No new randomness surface.** No dungeon-exclusive gear, pets, mounts or perks. Nothing here is a chase. |
| **C7** | **Uses the die.** It is still this game. A dungeon is a short board, not a menu button. |

---

## 2. The three dungeons

| ID | Name | Pays | Flavour |
|---|---|---|---|
| `DGN_STONEVAULT` | **The Stonevault** | **Enhance Stones** | A collapsed mine. Everything in it is made of the thing you want. |
| `DGN_FEEDPITS` | **The Feeding Pits** | **Beast Feed** | Something bigger used to eat here. |
| `DGN_MINT` | **The Old Mint** | **Crowns** | Still stamping coins. Nobody told it the kingdom fell. |

**Three, and only three.** They map exactly to the three materials that bottleneck the three meta systems: Enhance Stones gate the Forge, Beast Feed gates the Menagerie, Crowns gate merging. Merge Dust is not included — salvage already produces it in volume. **Soul Shards are deliberately not farmable**; they remain the pacing valve described in `10` §2, and a Soul Shard dungeon would delete that role.

---

## 3. Structure of a dungeon run

A compressed board. It reuses `GenerateBoard` (`03` §3) with a dungeon profile rather than a new system.

```
● ─ ● ─ ● ─ ● ─ ● ─ ● ─ ● ─ ● ─ ★
1   2   3   4   5   6   7   8   Guardian
```

| Property | Value |
|---|---|
| Nodes | **8 + 1 Guardian node** 📐 |
| Stages | 1. No stage gates, no interstitial. |
| Forks | **None.** The decision layer is the tier choice made before entry, not in-dungeon. |
| Tile composition | Fixed, not weighted: nodes 1–8 are `TILE_ENEMY` ×5, `TILE_CACHE_DUNGEON` ×2, `TILE_SHRINE` ×1, shuffled with the constraint that the Shrine is never node 1 or node 8 |
| `TILE_CACHE_DUNGEON` | A new tile type — pays a fixed portion of the dungeon's material. Not a random cache. |
| Guardian | An Elite-tier encounter (`05` §6.2) at **2.6× tier power**, with one Elite Modifier drawn from the standard pool. Not a boss — no phases, no authored mechanics. |
| Perk drafts | ✅ Yes, from the standard pool. A dungeon is short enough that a build never really forms, which is the point: dungeons are where gear and talents carry you, not perks. |
| Fixed dice | **1** 📐 fixed-die choice at the start of the run, no refresh. It took the seat the reroll allotment left (`16` D41): a dungeon is eight nodes and one Guardian, so one guaranteed landing is worth more here than on a full board. The number is the player's, named through `CHOOSE_FIXED_DIE`. |
| Energy cost | **10** 📐 (half a run) |
| Duration | 2–4 minutes |
| Death | Pays out at the clear multiplier for the last node reached (§4.3). Revive is **not** offered and `AD_REVIVE` does not apply. |
| Abandon | Pays nothing and refunds nothing. |

### 3.1 Why a Guardian rather than a boss

The Guardian exists to make the dungeon a **power check**: a player whose gear has fallen behind will stall on the Guardian and bank the 54% collected en route (§4.2), which is a legible signal to go and upgrade. A full authored boss would need eight of them across the tier ladder and would drag the dungeon past its four-minute budget.

---

## 4. Rewards

### 4.1 The tier ladder

Each dungeon has **8 tiers**, unlocked one-to-one by chapter clears. A player who has cleared Chapter 5 on Normal may enter dungeon tiers 1–5.

```
DungeonPower(t)  = ChapterPowerTarget(t) * 1.15          // t = 1..8, per 02 §4.1
DungeonYield(t)  = BaseYield * 2.0^(t-1)
```

| Tier | Enhance Stones | Beast Feed | Crowns |
|---|---|---|---|
| 1 | 30 | 60 | 900 |
| 2 | 60 | 120 | 1,800 |
| 3 | 120 | 240 | 3,600 |
| 4 | 240 | 480 | 7,200 |
| 5 | 480 | 960 | 14,400 |
| 6 | 960 | 1,920 | 28,800 |
| 7 | 1,920 | 3,840 | 57,600 |
| 8 | 3,840 | 7,680 | 115,200 |

📐 TUNABLE — every figure. These are placeholders shaped to the ×2 chapter curve and **must** go through the economy simulator (`21`) before they are treated as real. See §7.

**The player always enters the highest tier they have unlocked**, by default. Lower tiers remain selectable but there is never a reason to pick one, so the UI presents the ladder as information rather than as a choice: one button, "ENTER — Tier 5".

### 4.2 Payout distribution across the board

The dungeon's tier yield is paid out along the board so that progress is felt continuously rather than at the end:

| Source | Share of `DungeonYield(t)` |
|---|---|
| Each `TILE_ENEMY` kill (×5) | 6% each = 30% |
| Each `TILE_CACHE_DUNGEON` (×2) | 12% each = 24% |
| Guardian kill | 46% |

A player who dies to the Guardian therefore banks 54% — a meaningful consolation that still makes the Guardian worth beating.

### 4.3 Death and partial clears

```
DungeonPayout = Σ (yield already collected from resolved nodes)
```

There is no completion multiplier and no penalty. What the player picked up is theirs. This differs deliberately from chapter runs (`02` §5.2), where the multiplier creates run tension — a dungeon should have no tension, only throughput.

### 4.4 Legend XP — deliberately poor

```
DungeonLegendXp(t) = ChapterLegendXp(t) * 0.20
```

📐 TUNABLE. Constraint **C4** requires that a player who exclusively farms dungeons falls behind on Legend Level. The economy simulator must assert this (§7, E8).

### 4.5 What dungeons never pay

Gear · Soul Shards · Honor · Pet Eggs · Mount Crates · Merge Dust · Talent Points · Set Tokens · Beast Marks · pity counter progress of any class.

🔒 A dungeon advances **no counter in `24_LUCK_PROTECTION.md`**. Dungeons and luck protection are the two halves of the same guarantee and must not feed each other, or the anti-farming rule (`24` §1.2) is broken by the back door.

---

## 5. Entries and gating

| Property | Value |
|---|---|
| Entries per dungeon per day | **3** 📐 |
| Total daily entries | 9 across the three dungeons |
| Refresh | 05:00 UTC, matching PvP attempts (`11` §4.1) |
| Carry-over | **None.** Unused entries are lost. |
| Ad entries | `AD_EXTRA_DUNGEON` — **+1 entry, 3 per day**, spendable on any dungeon. New placement, catalogued in §6. |
| Slay Plus | 12 total entries/day, auto-granted — **identical to a full ad-watcher**, per the fairness contract (`12` §1) |
| Unlock | Legend Level **8**, alongside the Forge — dungeons are meaningless before there is something to spend materials on |

**Entries, not Energy, are the real gate.** The Energy cost exists so dungeons trade against runs; the entry cap exists so dungeons cannot become the whole game. At 9 entries × 10 Energy = 90 Energy per day, a player who does everything spends roughly one full tank on dungeons and still has their regeneration for runs.

---

## 6. New ad placement

Appended to the catalogue in `12_MONETIZATION_ADS.md` §4.2 as placement **#29**:

| # | ID | Location | Reward | Cap |
|---|---|---|---|---|
| 29 | `AD_EXTRA_DUNGEON` | Dungeon select screen | +1 dungeon entry, any dungeon | 3/day |

This takes the meta placement count to 16 and the total catalogue to **29 placements**, with maximum meta impressions rising from 33 to 36/day. The global daily soft cap rises to 44 (`12` §4.3) so it still sits above the meta total, preserving the cap's stated purpose.

🔒 Per `12` §1, the reward is capped, a subscriber receives exactly the capped amount, and the placement grants **materials only** — it accelerates no drop rate and touches no pity counter.

---

## 7. Impact on the economy simulator

`21_ECONOMY_SIMULATOR_SPEC.md` gains five requirements. Dungeons are a large, deterministic, uncapped-by-luck income stream and they will move every material curve in the game.

| # | Requirement |
|---|---|
| **E6** | Model all 14 player profiles (`21` §5.4) with and without dungeon play. Report Enhance Stone, Beast Feed and Crown balances for both. |
| **E7** | New assertion: **dungeons supply 40–65% of a mid-game player's Enhance Stone and Beast Feed income.** Below 40% and they are not worth the entry cap; above 65% and chapter runs stop mattering as a material source. |
| **E8** | New assertion: **a dungeon-only player reaches Legend Level milestones at most 0.6× as fast as a chapter-only player** at equal Energy spend. This is constraint C4, expressed as a test. |
| **E9** | Re-run the merge and enhancement cost curves in `08` §4. Deterministic Stone income makes `+11 → +15` materially cheaper in wall-clock terms, and the enhancement mercy stacking in `24` §4.6 compounds that. Both together may make `+15` too easy; the fix is the cost table, not the mercy. |
| **E10** | Re-run the Crown bottleneck target in `10` §4 ("Crown-constrained on merging"). `DGN_MINT` may remove that bottleneck, which would remove merging's status as the most exciting sink. |

⚠️ **E9 and E10 are the two most likely to fail.** Expect to raise `MergeCrownCost` and the `+11 → +15` stone costs after the simulator runs. Do not pre-emptively tune them by hand.

---

## 8. Screens

Two additions to `13_UI_UX_SCREENS.md` §1:

| # | Screen | Purpose |
|---|---|---|
| **S28** | **Dungeon Select** | Three dungeon cards, each showing its material icon, unlocked tier, the exact fixed payout for that tier, entries remaining (`2/3`), Energy cost, and the `AD_EXTRA_DUNGEON` button. One tap to enter. |
| **S29** | **Dungeon Board** | A reskin of S05 with the fork UI, stage pips and shop affordances removed, and a persistent material-collected counter in the HUD replacing the Gold counter. |

**Entry point:** a fourth item on the Chapter Select screen (S04) — *"Dungeons"* — plus a badge on the Home screen bottom nav when entries are unused. It is deliberately **not** a sixth bottom-nav item; the nav is full at five and dungeons are a daily errand, not a hub.

The payout must be stated as a **literal number before entry** — *"Tier 5 · 480 Enhance Stones"* — not as a range and not as an icon. The entire value of this feature is that the player knows exactly what they are getting.

---

## 9. Art and audio cost

Deliberately near-zero, which is why this feature is affordable.

| Asset | Approach |
|---|---|
| Dungeon biomes | **Reuse existing chapter biome art** with a palette shift: Stonevault → `pal_clockwork`, Feeding Pits → `pal_bloom`, Old Mint → `pal_astral`. No new backdrops. |
| Enemies | Existing archetypes and biome variants (`05` §6.1). No new enemies. |
| Guardian | An existing Elite sprite at 1.25× scale with a gold outline shader. **No new Guardian art.** |
| `TILE_CACHE_DUNGEON` icon | **1 new tile icon**, following `15` §E8 |
| Dungeon select cards | 3 illustrations 📐 — the only genuinely new art |
| Music | Reuse the three source biomes' tracks (`20`) |
| SFX | 1 new: the cache-collect chime |

**Net new asset cost: 4 art, 1 audio.** Against the 975 + 106 baseline this is a rounding error, and it is the reason a whole new mode fits in v1.

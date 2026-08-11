# 10 — Economy, Energy & Progression

🔒 **The founding constraint: no currency in this game can be bought with money.** The only real-money product is the Slay Plus subscription, which buys time and attention — never power. Every number below must be tuned as if the player will never spend a cent — because most of them can't.

---

## 1. Currency list

| Currency | Scope | Earned from | Spent on | Purchasable? |
|---|---|---|---|---|
| **Gold** | Run-local, wiped at run end | Enemies, treasure, events, minigames | Shop tiles only | ❌ |
| **Crowns** | Meta soft | Runs, quests, treasure, salvage, ads | Merging, pet levelling, inventory expansion, earned-currency shop | ❌ |
| **Soul Shards** | Meta premium (earned only) | Bosses, first clears, PvP seasons, weekly challenge | Pet Eggs, Mount Crates, S-tier gear chests, Energy refills | ❌ |
| **Energy** | Gate | Regeneration, ads, daily refill, level-up | Entering runs | ❌ |
| **Enhance Stones** | Material | Treasure, quests, salvage refund, ads | Gear enhancement | ❌ |
| **Merge Dust** | Material | Salvaging gear | Substituting a merge input | ❌ |
| **Beast Feed** | Material | Caches, quests, duplicate pets and mounts, ads | Pet and mount levelling | ❌ |
| **Honor** | PvP | Ghost Duels, season rank | Honor Shop (Pet Eggs, Mount Crates, gear chests, materials) | ❌ |

**Eight currencies.** 🔒 Pet Food and Mount Feed were merged into a single **Beast Feed** (decision D17 in `16_DECISION_LOG.md`), removing a currency players would have touched a dozen times in their whole account life.

### 1.1 Two counters that are not wallet currencies

`24_LUCK_PROTECTION.md` §7 introduces **Beast Marks** (from ★5-duplicate pets and duplicate mounts → buy a chosen pet) and **Set Tokens** (from SS salvage and SS duplicates → buy a chosen SS gear item). Both are mercy-accrual counters (`24` §1, M3), and both are deliberately **kept out of the wallet**: single-sink, single-screen, never in the global currency header, never in a shop tab. They are progress bars counted in units, not currencies, and the eight-currency count above is unchanged.

`26_LIVE_OPS_AND_EVENTS.md` adds a **per-event currency** which exists only while its event is live and auto-converts to Crowns at close (`26` C4). It is also not a wallet currency.

⚠️ **SCHEDULED REVIEW:** eight is still at the upper edge of what a player can hold in their head. After the first playtest, review whether **Merge Dust** and **Enhance Stones** should also merge into a single "Forge Material" — they are spent in the same screen, on the same items, by the same player, at the same time. That would bring the count to seven. A reminder for this review has been scheduled.

---

## 2. Soul Shards — the "premium" currency that isn't

Soul Shards occupy the slot that would normally be the paid currency. Because they cannot be bought, their entire design purpose changes: they are a **pacing valve**, not a revenue lever.

| Source | Amount |
|---|---|
| Chapter boss kill (Normal) | 15 × chapter |
| Chapter boss kill (Heroic/Mythic) | 40 / 100 × chapter |
| First clear bonus | 100–800 depending on chapter/tier |
| Daily quests (3 × 20, when the Soul Shard reward is drawn) | up to 60 |
| Daily quest 3-of-3 bonus chest | 100 |
| Weekly challenge | 400 |
| PvP season rank reward | 200–3,000 |
| Daily login | 30–500 |

| Sink | Cost |
|---|---|
| Pet Egg | 900 |
| Mount Crate | 2,500 |
| S-tier Gear Chest | 1,800 |
| Energy refill (full) | 300, rising +150 per use per day, resets daily |
| Inventory expansion (+20) | 400 (alternative to Crowns) |

Pet Egg, Mount Crate and S-tier Gear Chest purchases grant **stored containers** onto the unopened shelf (S19 / S16), opened later via `OPEN_EGG` / `OPEN_CRATE` / `OPEN_CHEST` — odds, pity and Focus are read **at open**, not at purchase (`24` §4.0, ruled in `16` A7).

Design target: a daily active player earns **~700–1,100 Soul Shards/day** in mid-game — roughly one Pet Egg a day, or an S-gear chest every other day.

📐 TUNABLE.

---

## 3. Energy 🔒

| Property | Value |
|---|---|
| Max Energy | 120 (+2 per Legend Level, cap 200) 📐 |
| Regeneration | 1 per 4 minutes (15/hour) |
| Full refill time | 8 hours from empty |
| Run cost | 20 |
| Runs on a full tank | 6 |
| Regen while offline | ✅ yes — this is the *only* offline accrual in the game |
| Regen cap | Energy stops at max. Overflow is **not discarded** — it banks into the **Energy Reserve** (below). |
| **Energy Reserve** | A second bank holding **1× Max Energy** (200 at cap). Receives overflow only, never regenerates on its own, and is drawn automatically when the main bar cannot cover a run or dungeon. **`28_LIVE_SERVICE_ESSENTIALS.md` Part C** is the authority. |

⚠️ This section previously said *"no overflow banking"* in the same breath as *"a returning lapsed player should always find a full tank"*. Those contradicted each other: a player away for two days regenerated 720 Energy and kept 200. The Reserve resolves it without adding a second exception to D2 — nothing new accrues, less is discarded.

### 3.1 Energy sources beyond regen

| Source | Amount | Cap |
|---|---|---|
| `AD_ENERGY` rewarded ad | +40 | 4/day |
| Daily free refill (first login of the day) | to full | 1/day |
| Legend Level up | to full | — |
| Daily quests | +20 each | 3/day |
| Soul Shard refill | to full | escalating cost |
| `TILE_EVENT` (rare outcome) | +10 | — |

### 3.2 Daily play budget

| Player type | Energy/day | Runs/day | Playtime |
|---|---|---|---|
| Free, no ads | 120 (start) + 180 (regen over 12h) + 60 (quests) + 120 (daily refill) ≈ **480** | ~24 | Realistically caps at ~8–10 runs of actual sitting time |
| Free, watches ads | +160 → **640** | ~32 | ~2.5–3 h |
| Slay Plus | same as ad-watcher, auto-granted | ~32 | ~2.5–3 h |

**Key insight to preserve:** Energy in Slay Idle Repeat is a *soft* pacing tool, not a hard wall. A committed player should almost never be stopped by it; a returning lapsed player should always find a full tank. If telemetry shows the median session ends because of Energy exhaustion more than 25% of the time, raise the cap or the regen rate.

🔒 There is **no** mechanism to buy Energy with money, directly or indirectly.

---

## 4. Crowns

The workhorse soft currency.

| Source | Approx. per run (mid-game) |
|---|---|
| Run completion | 400–900 |
| Treasure tiles | 200–600 |
| Salvage | 300–1,200 |
| Daily quests | 1,500 |
| `AD_CROWNS` (3/day) | 600 each |

| Sink | Approx. cost |
|---|---|
| Merge to B / A / S / SS | 120 / 600 / 3,000 / 15,000 |
| Pet level-up | 50 → 4,000 escalating |
| Inventory expansion | 800 → 6,000 escalating |
| Daily-tab offers and staples (§5.1) | 450–2,500 |

Design target: a mid-game player should be **Crown-constrained on merging** but never Crown-starved for pet levelling. Merging is the intended bottleneck because it's the most exciting sink.

**Reconciliation 🔒 (ruled in `16` A7):** the income rows above are the *product* of the authored in-run tables in `03` §7a, not independent numbers. At the mid-game reference point (Chapter 4–5, Heroic, ~2.5 treasure tiles per run) the authored tables produce: run completion ≈ **550–750** Crowns (row band 400–900 ✓) and treasure ≈ **460–620** Crowns (row band 200–600 ✓). Two clarifications to the rows: the `AD_CROWNS` figure of 600 is the **BaseValue anchor** (`12` §5) — the actual grant scales ×(1 + 0.35 × highestChapterCleared); and the two escalating sinks are now authored curves:

```
Pet level-up Crowns:      CrownCost(level) = 50 × level^1.075        // `07` §2.2 — 50 at level 1→2,
                                                                     // ≈4,000 at 59→60 (beasts.json)
Inventory expansion (+20 slots each, k = 1..10):                     // currencies.json 📐
    800 · 1,000 · 1,250 · 1,560 · 1,950 · 2,440 · 3,050 · 3,810 · 4,770 · 6,000
    (≈ ×1.25 per step; hard cap 10 purchases = +200 slots)
```

The flat **400 Soul Shard** alternative (§2) applies at any step — deliberately the better deal from step ~4 on, so the scarcer currency buys convenience, never power.

---

## 5. The Shop (earned currency only)

The shop screen has **four tabs**. Only tab 4 involves real money.

| Tab | Contents | Pays with |
|---|---|---|
| **Daily** | **Staples** (always in stock) + **6 rotating offers** (§5.1) | Crowns, Soul Shards |
| **Honor** | PvP-exclusive: Pet Eggs, Mount Crates, gear chests, materials (`11` §7) | Honor |
| **Materials** | Enhance Stones, Merge Dust, Beast Feed at fixed rates (§5.2) | Crowns |
| **Plus** | **Slay Plus** — the only paid product in the game (€4.99/month) | Real money |

The Plus tab contains exactly one item. No bundles, no "best value" badge, no countdown timers, no fake discounts. See `12_MONETIZATION_ADS.md` §2.

### 5.1 The Daily tab 🔒 (ruled in `16` A7)

**Staples — permanent fixtures, never rotated out** (prices are the §2 sink table's; `21` §7's purchase policy assumes these are always purchasable):

| Staple | Price |
|---|---|
| Pet Egg | 900 Soul Shards |
| Mount Crate | 2,500 Soul Shards |
| S-tier Gear Chest (`CHEST_PREMIUM`, `24` §4.0a) | 1,800 Soul Shards |
| Energy refill (full) | 300 Soul Shards, +150 per use per day, resets daily |

**Rotating offers** — 6 per day from the authored pool of 10 below. 📐 All in `data/tuning/currencies.json`. Material amounts scale ×`(1 + 0.35 × highestChapterCleared)` — the ad-bundle scalar from `12` §5. Prices are fixed.

| ID | Offer | Price | Limit |
|---|---|---|---|
| `DLY_RANDOM_S` | One random S-rarity gear item (deterministic rarity, random identity; Focus ×2.5 applies; no counter class — `24` §4.0a) | 1,400 Soul Shards | 1/day |
| `DLY_CHEST_STD` | 1 × gear chest (`CHEST_STANDARD`) | 1,200 Crowns | 1/day |
| `DLY_CHEST_PREM` | 1 × S-tier Gear Chest (`CHEST_PREMIUM`) | 1,500 Soul Shards | 1/day |
| `DLY_EGG` | Pet Egg | 750 Soul Shards | 1/day |
| `DLY_CRATE` | Mount Crate | 2,100 Soul Shards | 1/day |
| `DLY_STONES` | Enhance Stones ×30 (scaled) | 700 Crowns | 1/day |
| `DLY_DUST` | Merge Dust ×120 (scaled) | 650 Crowns | 1/day |
| `DLY_FEED` | Beast Feed ×90 (scaled) | 650 Crowns | 1/day |
| `DLY_STONE_BULK` | Enhance Stones ×80 (scaled) | 500 Soul Shards | 1/day |
| `DLY_DUST_BULK` | Merge Dust ×300 (scaled) | 450 Soul Shards | 1/day |

**Draw rule** 📐: `DLY_RANDOM_S` is always among the day's 6; the other 5 are drawn without replacement from the remaining 9, subject to **at most 3 Soul-Shard-priced offers per day** (counting `DLY_RANDOM_S`). The draw is seeded from the day's quest-draw seed (`BEGIN_SESSION`, `14` §2.3). `AD_SHOP_REDRAW` (1/day) redraws the entire 6-offer block, including a fresh `DLY_RANDOM_S` identity. The discounted egg/crate/chest offers exist so a rotation day can feel lucky; the staples above mean nobody ever *waits* to spend.

### 5.2 The Materials tab 🔒 (ruled in `16` A7)

Fixed rates, always available, priced so the tab is the *expensive* certainty against the Daily tab's cheap luck. 📐 The rates are exactly **×10⁄3 of the Honor Shop's per-unit prices** (`11` §7: 7.5 / 1.8 / 2.4 Honor per Stone / Dust / Feed), so the two material shops express one exchange ratio in two currencies:

| Material | Price | Daily cap 📐 |
|---|---|---|
| Enhance Stone | 25 Crowns each | 40 |
| Merge Dust | 6 Crowns each | 250 |
| Beast Feed | 8 Crowns each | 200 |

The daily caps keep Crowns from becoming a universal material converter — without them, treasury players would liquefy the merge bottleneck (§4) through this tab.

---

## 6. Daily quests

3 per day, drawn from a pool of **20 authored quests**. One free reroll.

✅ **The full pool is authored in `19_CONTENT_TABLES.md` Part B**, including the draw rule (always 1 Easy + 1 Medium + 1 Easy-or-Medium, with Hard quests reachable only via reroll at ×2 reward).

Rewards per quest: Crowns 500, Energy +20, and one of {Enhance Stones ×15, Beast Feed ×30, Soul Shards ×20}. Completing all 3 grants a bonus chest.

`AD_DOUBLE_QUEST` doubles one quest's reward, 3/day.

---

## 7. Chapter and tier gating

```
Chapter c Normal   unlocked by: clear Chapter c-1 Normal
Chapter c Heroic   unlocked by: clear Chapter c Normal
Chapter c Mythic   unlocked by: clear Chapter c Heroic  AND  Legend Level 60
```

There is **no** level gate on Normal chapters. If a player can beat Chapter 5 at Legend Level 20 through skill and lucky drops, let them. Under-levelled players will simply fail and be pushed back to grinding naturally — that's a better teacher than a locked door.

---

## 8. Progression pacing model

🔒 **The canonical pacing curve lives in `29` §6 (`expected_progression.json`), graded by `21` A1: the full ad-watcher clears Chapter 8 Normal on ~day 25–45.** (Ruled in `16` A7.) `01` §7 is the player-facing summary of that same curve. This section describes the *shape* the reward tables produce — where it and `29` §6 disagree, `29` §6 wins.

The raw, pre-catch-up shape, as time for a **free no-ads player** to reach par power for chapter `c`:

```
TimeToChapter(c) ≈ 0.55 * 2^(c-1) hours of active play    (no catch-up, no ads)
```

| Chapter | Cumulative hours | ≈ Days at 1 h/day |
|---|---|---|
| 1 | 0.5 | 1 |
| 2 | 1.1 | 1 |
| 3 | 2.2 | 2 |
| 4 | 4.4 | 4 |
| 5 | 8.8 | 9 |
| 6 | 17.6 | 18 |
| 7 | 35 | 35 |
| 8 | 70 | 70 |

Uncorrected, the tail is far too steep: day ~70 for the no-ads player is well outside the canonical bands (ad-watcher 25–45; no-ads 35–55, `01` §7). **A catch-up curve closes the gap:** rewards from chapters below the player's highest cleared chapter are reduced by 60% 📐, while the *newest* chapter pays a "frontier bonus" of +50% 📐. The curve's success criterion is landing both `01` §7 columns — it no longer has an independent day target of its own.

📐 TUNABLE. ⚠️ **NEEDS DETAIL:** the frontier-bonus/catch-up curve is sketched, not specified — it cannot be authored by hand, and deriving it is the economy simulator's first job (**O1**, `21` §12).

---

## 9. Required: the economy simulator

✅ **Fully specified in `21_ECONOMY_SIMULATOR_SPEC.md`.** It is a C# tool in `tools/EconomySim` that is a thin wrapper over `InMemoryGame` — it references **`SlayIdleRepeat.Core` only** (`30` §6) with the real `game-data`, over 14 behavioural profiles and 180 simulated days, with 16 named CI assertions plus 23 inherited requirements that fail the build on a broken economy.

This is the tool that turns every 📐 TUNABLE in this documentation set into a real number. Until it has been run, **all economy numbers in this document are informed guesses** — including the ones that look precise.

---

## 9a. Three new material income streams ⚠️

Three documents added after this one each introduce a material income stream, and each was sized independently:

| Stream | Doc | Pays | Shape |
|---|---|---|---|
| **Resource Dungeons** | `25` | Enhance Stones, Beast Feed, Crowns | Deterministic, 9 entries/day, 10 Energy each |
| **Events** | `26` | Crowns, Stones, Beast Feed, Soul Shards, Set Tokens | Rolling calendar, always one live |
| **Guilds** | `27` | Crowns, Stones, Beast Feed + up to +8% multipliers | Daily Guild Chest, weekly Guild Boss |

⚠️ **They compound, and guild perks are multiplicative on top of the other two.** Taken together they may double a mid-game player's Crown and Beast Feed income, which would remove the merge bottleneck this document identifies in §4 as the game's most exciting sink.

🔒 **Do not tune any of the three individually.** Run the economy simulator with all three enabled, plus the new sinks from `24` §6 (Reforge and Retune), and re-derive `MergeCrownCost`, the `+11 → +15` Enhance Stone costs and `BeastFeedCost` together. This is assertion **E19** and it is the highest-risk item in the whole simulator spec.

Energy pressure also changes: 9 dungeon entries × 10 Energy = 90 Energy/day on top of runs. §3.2's daily budget table must be recomputed — a player who does everything is now genuinely Energy-constrained, which may be correct or may need the regen rate raised. The simulator decides.

---

## 10. Energy and the online requirement

Because PvE is server-authoritative (`14` §2), Energy regeneration is computed **server-side from wall-clock time**, not from device time. This removes the classic clock-manipulation exploit entirely and means the player's Energy is correct on every device the moment they log in.

The trade: a player with no connection cannot spend Energy, because they cannot play. Energy still accrues while they are away. See `14` §3 for connection handling.

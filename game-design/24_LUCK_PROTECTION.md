# 24 — Luck Protection

🔒 **Founding rule: no random source in this game may be able to starve a player indefinitely.**

`10_ECONOMY_AND_PROGRESSION.md` establishes that nothing can be bought. That makes randomness the *only* remaining way a player can fall behind through no fault of their own — and therefore the only remaining source of the feeling this game exists to remove. A game with no wallet shortcut must not have a luck shortcut either.

This document is the single authority for every pity, mercy, streak-breaker and floor in the game. Where another document states a pity rule, **this document supersedes it** and the other document's rule is repeated here with a cross-reference.

---

## 1. The three mechanisms

Everything in §4 is built from exactly three primitives. Do not invent a fourth.

| # | Mechanism | Shape | When to use |
|---|---|---|---|
| **M1** | **Hard pity** | A counter of consecutive misses. On reaching `N`, the next draw is *forced* to satisfy the guarantee. Counter resets to 0. | Any discrete draw the player performs repeatedly: chests, eggs, crates, wheel spins, minigames. |
| **M2** | **Soft pity** | After threshold `T` misses, the target's weight is multiplied by `1 + k·(misses − T)` on each subsequent draw. | Layered *underneath* M1 on the rarest outcomes, so the guarantee rarely has to fire nakedly. Makes the curve feel lucky rather than mechanical. |
| **M3** | **Mercy accrual** | Each miss grants a small amount of a **deterministic substitute** — a token, a stone, progress. Enough accrual buys the thing outright. | Any chase where a hard guarantee would be too generous at the required `N` (SS gear, SS pets), or where the player must be able to *target* a specific item. |

**M1 is mandatory everywhere. M2 and M3 are applied selectively per §4.**

### 1.1 Rules that bind all three

| Rule | Specification |
|---|---|
| **Authority** | Every counter is a column on the server profile. The client displays; it never computes, never predicts, never resets. (`14` §2.1) |
| **Visibility** 🔒 | Every counter is **shown to the player, always**, as a plain sentence with a real number: *"Guaranteed A-rarity or better in 4 chests."* A hidden pity system is indistinguishable from no pity system and buys none of the goodwill it costs to build. |
| **Persistence** | Counters never reset on chapter change, tier change, season roll, app update, subscription lapse, or logout. They reset **only** when their guarantee fires. |
| **No decay** | Counters never tick down over time. Bad luck is not a debt that expires. |
| **Isolation** | Counters are **per source class** (§3). They never pool across classes, and one class's draw never advances another's counter. |
| **Disclosure** | Every rate and every `N` in §4 is stated in-game on the relevant screen, and in a single **Odds & Guarantees** page in Settings. This is a store-policy requirement for randomised rewards on both platforms; it is also simply the honest thing to do. |
| **Determinism** | Pity resolution happens inside `SlayIdleRepeat.Core` from a server-issued seed, and is therefore reproducible and testable like everything else. (`14` §8) |

### 1.2 The anti-farming rule 🔒

Counters are per **source class**, never global, for one reason: if a cheap source and an expensive source shared a counter, the optimal play would be to spam the cheap source until the counter is nearly full, then spend on the expensive one. That converts a fairness system into a chore.

**Test to apply to every new counter:** *can the player advance this counter more cheaply than by using the thing it protects?* If yes, the counter is scoped wrong.

---

## 2. What luck protection is not

Recorded explicitly, because each of these is a plausible-sounding mistake.

- ❌ **Not a rate increase.** Base rates in `08` §2 and `07` §2.2 are unchanged. Pity bounds the *tail*, it does not shift the *mean*. The economy simulator (`21`) must be re-run after this document lands precisely because pity raises the floor without touching the average, and the floor is what most players actually experience.
- ❌ **Not purchasable, accelerable or resettable with money.** There is no product that advances, resets or protects a counter. (`12` §8)
- ❌ **Not an ad reward.** No placement in `12` §4 may grant pity progress. `AD_ELITE_GUARANTEE` and `AD_ENHANCE_LUCK` remain as specified — they are one-shot boosts to a single roll, and neither touches a counter.
- ❌ **Not a replacement for a sink.** Where a chase is too long, the fix is the drop table or the merge cost, not a shorter pity. Pity is the safety net, not the ladder.

---

## 3. Source classes

Every randomised grant in the game belongs to exactly one class. Classes are the unit of counter isolation (§1.2).

| Class | Members | Counter key |
|---|---|---|
| `CHEST_STANDARD` | `AD_FREE_GEAR_CHEST`, daily-quest bonus chest, daily-login gear chests (except the day-14/28 S-tier chests), Lucky Wheel segment 7, `MG_CHEST_PICK` rewards that grant gear | `chest.standard` |
| `CHEST_PREMIUM` | S-tier Gear Chest (Soul Shards), Honor Shop S Gear Chest, PvP season gear chests, weekly-challenge chest, the day-14/28 S-tier login-calendar chests (`19` Part G) | `chest.premium` |
| `CHEST_APEX` | Honor Shop SS Gear Chest, Legend-tier season chest, chapter first-clear Mythic chest | `chest.apex` |
| `DROP_RUN` | All in-run gear: elite kills, boss kills, normal-enemy drops, `TILE_TREASURE` gear | `drop.run` |
| `EGG_PET` | Pet Eggs from every source | `egg.pet` |
| `CRATE_MOUNT` | Mount Crates from every source | `crate.mount` |
| `ENHANCE` | Gear enhancement attempts, `+6` and above | per gear instance |
| `DRAFT` | In-run perk drafts | per run |
| `WHEEL` | Lucky Wheel spins | `wheel` |
| `MINIGAME` | `MG_CHEST_PICK` outcome tier | `minigame.chestpick` |

📐 TUNABLE: class membership lives in `data/luck.json`. Adding a new grant source **must** assign it a class; the content schema validator fails the build if a grant source has no class.

---

## 4. The catalogue

### 4.1 `CHEST_STANDARD` — the ten-chest ladder 🔒

This is the headline guarantee and the one most players will actually feel.

| Counter | Guarantee |
|---|---|
| Every **10th** chest | At least one item at **A-rarity or better** |
| Every **40th** chest | At least one item at **S-rarity or better** |
| Every **160th** chest | At least one item at **SS-rarity** |

**Interaction rule:** the three counters run independently and simultaneously. Chest #40 satisfies both the 10-counter and the 40-counter and resets both. Chest #160 resets all three.

**Overshoot rule:** a natural drop that meets or exceeds a guarantee resets that counter. Rolling a natural S on chest #7 resets both the 10-counter and the 40-counter. The player is never punished for good luck by having a guarantee taken away later — they are simply already ahead of the schedule.

**Soft pity (M2) on the 160-counter:**
```
ssWeight(misses) = baseSsWeight × (1 + 0.05 × max(0, misses − 100))
```
so the SS chance begins climbing from chest 101 and reaches roughly 4× base by chest 159. In practice the hard guarantee fires rarely; the player feels a streak of near-misses turn into a hit, which reads as luck rather than as a vending machine.

📐 TUNABLE: `10 / 40 / 160`, the `100` threshold and the `0.05` slope.

### 4.2 `CHEST_PREMIUM` and `CHEST_APEX`

Same ladder shape, compressed, because these chests are already expensive.

| Class | Guarantee |
|---|---|
| `CHEST_PREMIUM` | Every **5th** chest: S or better. Every **25th**: SS. Soft pity on SS from miss 15, slope `0.08`. |
| `CHEST_APEX` | Every **3rd** chest: SS. No soft pity needed at that density. |

### 4.3 `DROP_RUN` — the dry-streak breaker

In-run drops are the highest-volume randomness in the game and the one most likely to produce a session that feels like nothing happened. Two protections, both scoped to the whole account, not to the run:

| # | Rule |
|---|---|
| **D1** | **Elite mercy.** Count consecutive Elite kills whose drop was below A-rarity. On the **6th**, force A or better. Resets on any natural A+. |
| **D2** | **Boss mercy.** Count consecutive boss kills whose best drop was below S. On the **4th**, force S or better. Resets on any natural S+. |
| **D3** | **Session floor.** If a completed run (Victory or a Stage-3 death) produced **zero** items at B-rarity or better, the run-end payout adds one guaranteed B item at chapter-appropriate power. This fires at most **twice per day** and is announced on the results screen as *"The road owed you one."* |

D3 is the anti-"that run was a waste" valve. It is cheap, it fires rarely for an equipped mid-game player, and it is worth more to retention than its expected value suggests.

### 4.4 `EGG_PET` — supersedes `07` §2.2

The existing rule (guaranteed S every 30, SS every 150) stands, with three additions:

| # | Addition |
|---|---|
| **P1** | **Duplicate protection on the guarantee.** When a pity-forced egg resolves, it must produce a pet the player does **not yet own**, if any unowned pet exists at the guaranteed rarity or above. Natural rolls are unchanged and may duplicate. |
| **P2** | **Mercy accrual (M3).** Every egg that produces a duplicate at ★5 (i.e. pure Beast Feed, no progress) also grants **1 Beast Mark**. **60 Beast Marks** buy any pet of the player's choice at B/A rarity, **200** buy any S, **600** buy any SS, from a **Menagerie exchange** counter that is always visible. This makes the *specific* pet a player wants reachable by pure persistence, which no amount of rarity pity can do. |
| **P3** | **Newcomer floor.** A player's first **3** eggs are forced to be 3 *distinct* pets. A new account whose first three eggs are the same B pet has a bad time for no reason. |

### 4.5 `CRATE_MOUNT` — new, was unprotected

`07` §3 specifies no pity at all for mounts, which is the single largest unprotected chase in the game: 12 mounts, 2,500 Soul Shards per crate, and the SS mounts (`MNT_VOIDSTEED`, `MNT_FATESPINNER`, `MNT_WORLDBEARER`) carry run-defining effects.

| Property | Value |
|---|---|
| Odds | A 70% · S 26% · SS 4% 📐 |
| Hard pity | Guaranteed S or better every **8** crates; guaranteed SS every **30** |
| Soft pity | SS weight from miss 20, slope `0.10` |
| Duplicate protection | The pity-forced crate must yield an unowned mount at that rarity if one exists |
| Mercy accrual | Duplicate mounts grant **300 Beast Feed** (as `07` §5) **plus 1 Beast Mark**, feeding the same exchange as P2 |

### 4.6 `ENHANCE` — failure mercy, supersedes `08` §4.2

`08` §4.2 sets `+15` at a 25% success rate with no protection. At that rate a player will routinely eat six or seven consecutive failures, and since enhancement is a pure material sink with no item risk, those failures read as the game deleting resources for nothing.

**Mercy stacking (M2), per gear instance:**
```
effectiveRate = min(1.00, baseRate + 0.08 × consecutiveFailuresOnThisItem)
```
- The counter is stored **on the gear instance**, so it is not farmable across cheap items (§1.2).
- It resets to 0 on success, and is **not** lost on salvage-and-remake — an item merged upward carries the counter of its highest-counter input, so a player is never punished for merging mid-chase.
- `AD_ENHANCE_LUCK`'s `+15` percentage points stack additively on top and do **not** advance or consume the mercy counter.
- Displayed as *"Success 41% (+16% mercy)"* — the bonus is broken out so the player can see the system working.

At `+15` this means a guaranteed success by the 10th attempt at worst, and a typical success around attempt 3–4.

📐 TUNABLE: the `0.08` slope.

### 4.7 `DRAFT` — supersedes `06` §4

The existing Legendary pity (draft #15) and Sustain anti-brick stand. Three additions:

| # | Addition |
|---|---|
| **F1** | **Quality floor.** If **3 consecutive drafts** contain no option above Common, force one Rare-or-better option into the next draft. This is the draft equivalent of D3 and stops the early-stage "three grey cards again" run. |
| **F2** | **Codex bias.** Perks the player has **never** drafted carry a `×1.35` weight multiplier in the fresh-pool draw, capped so that at most 1 of the 3 options is bias-selected. This accelerates Codex completion (`06` §6), rewards breadth, and makes early accounts see more of the game faster. |
| **F3** | **Upgrade famine.** The 30% owned-upgrade bias in `06` §4 is a per-option roll, so a player can go a whole stage without seeing a single upgrade. If **5 consecutive drafts** offer no owned-perk upgrade while the player owns at least one non-maxed perk, force one. |

### 4.8 `WHEEL` — supersedes `19` Part F

Jackpot pity at 60 spins stands. Add:

| # | Addition |
|---|---|
| **W1** | **No triple repeat.** The same segment may not be rolled three times consecutively; on the third, re-roll excluding it. Eight segments and three free spins a day makes visible repetition common, and repetition on a "gift" reads as a broken wheel. |
| **W2** | **Soft pity on the jackpot** from spin 40, slope `0.06`. |

### 4.9 `MINIGAME` — `MG_CHEST_PICK`

The three-chest pick is a pure 1-in-3. Guarantee the gold-tier chest every **4th** consecutive miss. The other three minigames are skill-scaled (`03` §6) and need no protection.

### 4.10 Board and encounter variance

Not pity in the strict sense, but the same principle: the generator must not be able to produce a hostile board.

| # | Rule | Where enforced |
|---|---|---|
| **B1** | Every run contains **≥ 2** `TILE_TREASURE` and **≥ 1** `TILE_CACHE`, injected after the weighted draw if the draw did not produce them. | `03` §3, new constraint C7 |
| **B2** | No Elite may draw the **same modifier** as the immediately preceding Elite in the same run. | `05` §6.2 |
| **B3** | The three PvP candidate Ghosts (`11` §4) must include **at least one** rated below the player. Three consecutive stronger opponents is a wasted attempt allowance. When no real candidate below the player exists (sparse population, or the lowest-rated player), a scaled bot Ghost fills the slot — `11` §4.4. | `11` §4.2, §4.4 |
| **B4** | `TILE_EVENT` outcomes: an event option's worst-weighted outcome may not fire **twice consecutively** for the same player across the same event ID. | `19` Part A |

---

## 5. Targeted acquisition — the Focus system 🔒

Pity bounds the tail. It does **not** solve the problem of wanting a *specific* thing: with 24 base items across 6 slots, a player chasing the last piece of a Stormcall SS set can be perfectly lucky on rarity and still never see the slot they need.

**Focus** is the answer, and it is free, permanent and unlimited.

| Property | Value |
|---|---|
| What | The player nominates **one slot** and **one family** (e.g. *Bow*, *Weapon*) as their Focus, on the Forge screen |
| Effect | Within any gear grant, once rarity has been determined, the **item identity** roll is biased: the focused `(slot, family)` receives a `×2.5` weight 📐 |
| Scope | Applies to `CHEST_STANDARD`, `CHEST_PREMIUM`, `CHEST_APEX` and `DROP_RUN`. **Does not** apply to pets or mounts. |
| Changing it | Free and instant, but a change puts Focus on a **12-hour cooldown** before the new selection takes effect — long enough to prevent per-chest swapping, short enough to never feel like a lock |
| Cost | None. Not gated by level, currency, ads or Plus. |
| Visibility | The Focus is shown on the Forge, Inventory and Run Results screens |

**Why weight rather than a guarantee:** a hard "focused items only" rule would collapse the loot table and destroy the pleasant surprise of an off-Focus drop. `×2.5` roughly halves the expected time to a specific piece without making anything else feel worthless.

### 5.1 Set Tokens — the SS set endgame (M3)

The 6-piece SS set bonuses in `08` §3.2 are the game's deepest chase, and under the current rules they require either six natural SS drops in the right slots, or `3 × S → SS` merges needing **three identical S items** each. That is a wall made entirely of luck.

| Property | Value |
|---|---|
| Source | Salvaging an **SS** item yields **1 Set Token** in addition to its Merge Dust. Duplicate SS from any chest class yields **1 Set Token**. |
| Sink | **12 Set Tokens** → choose **any** SS item: the player picks slot and family, and the item rolls quality and affixes normally. 📐 |
| Not | Not purchasable, not tradeable, not an ad reward, not granted by Plus. |
| Visibility | A permanent counter on the Forge: *"Set Tokens 7 / 12"* |

This gives the endgame a deterministic spine: a player who keeps playing will finish their set, on a schedule they can see, no matter how their luck runs.

---

## 6. Quality and affix protection

Rarity pity is worthless if the item that finally arrives rolls badly. `08` §3 gives every item a quality roll and 0–4 random affixes, with no way to influence either. Two operations close that, both in the Forge, both earned-currency only.

### 6.1 Reforge (quality)

| Property | Value |
|---|---|
| Effect | Re-rolls the item's quality value. **The result is the better of the two rolls** — quality can only go up. 🔒 |
| Cost | Merge Dust, scaling with rarity: `C 100 · B 300 · A 1,000 · S 4,000 · SS 12,000` 📐 |
| Limit | Unlimited attempts |
| Why "better of two" | A reforge that can make an item worse is a gamble; a reforge that only improves is a grind. This game chooses grind every time. |

### 6.2 Retune (affixes)

| Property | Value |
|---|---|
| Effect | Re-rolls **all unlocked** affixes on the item |
| Locking | Before a Retune the player may **lock** affixes they want to keep. Locking 1 costs ×2, 2 costs ×5, 3 costs ×12 the base price 📐 |
| Cost | Enhance Stones, scaling with rarity: `B 20 · A 60 · S 200 · SS 600` 📐 |
| Limit | Unlimited attempts |
| Mercy (M2) | Each Retune that produces no affix from the item's **wishlist** (the player marks up to 3 desired affix IDs per item) raises the wishlist-hit weight by `+10%`, resetting on a hit |

Together these mean an SS item is a **platform the player improves**, not a lottery ticket they either won or lost. That is the single biggest change this document makes to how the endgame feels.

---

## 7. New currencies introduced by this document

| Currency | Scope | Earned from | Spent on |
|---|---|---|---|
| **Beast Mark** | Meta | Duplicate pets at ★5, duplicate mounts | Menagerie exchange for a chosen pet (§4.4 P2) |
| **Set Token** | Meta | Salvaging or duplicating SS gear | Choosing any SS gear item (§5.1) |

⚠️ This takes the currency count from 8 to **10**, against `10` §1's own note that eight is already at the upper edge of what a player can hold in their head, and against the scheduled review **O10** which was considering going *down* to seven.

**Mitigating design, which must be implemented:** both new currencies are **single-sink, single-screen counters**, not wallet items. Beast Mark appears only on the Menagerie exchange row; Set Token appears only on the Forge. Neither is shown in the global currency header, neither has a shop tab, and neither is ever spent anywhere else. They are progress bars that happen to be counted in units — closer to the pity counters in §4 than to Crowns.

If the O10 review still wants seven wallet currencies, it can have them: these two are not in the wallet.

---

## 8. Anti-abuse

| Vector | Mitigation |
|---|---|
| Farming a cheap class to trigger an expensive guarantee | Counters are per class (§1.2, §3) |
| Salvage-and-remake to reset enhancement mercy | Mercy lives on the gear instance and is **inherited** by merge outputs (§4.6) |
| Focus-swapping per chest | 12-hour cooldown on Focus changes (§5) |
| Abandoning runs to fish for the D3 session floor | D3 requires Victory or a Stage-3 death, and is capped at 2/day |
| Client-side counter manipulation | Counters are server columns; the client renders a number it was told (`14` §2.1) |
| Re-rolling a duel to dodge B3 | Candidate sets are issued per attempt and consume the attempt |

---

## 9. Player-facing surfaces

| Screen | Addition |
|---|---|
| **S16 Inventory** | Focus selector; per-item Reforge / Retune entry points |
| **S17 Forge** | Two new tabs: **Reforge** and **Retune**. Set Token counter in the header. Focus row. |
| **S19 Menagerie** | Beast Mark exchange row with all three tiers and their counters |
| **S03 Home** | Chest-class counters shown on the chest widget: *"Guaranteed A in 4"* |
| **S14 Run Results** | D3 announcement line when it fires; `DROP_RUN` mercy counters in the reward tally footer |
| **S26 Settings** | **Odds & Guarantees** page: every rate and every `N` in this document, in plain language, in both launch languages |
| **S23 Shop** | Every chest listing states its class and the class's current counter before purchase |

📐 All counter strings are localisation keys. German runs ~30% longer (`13` §10) and these strings sit in tight widget rows — test them.

---

## 10. Impact on the economy simulator

`21_ECONOMY_SIMULATOR_SPEC.md` must be extended before any number here is treated as final. Pity systems change the **distribution** of outcomes far more than the mean, and the simulator currently reports means.

| # | New requirement |
|---|---|
| **E1** | Report the **10th percentile** player alongside the median for every progression milestone. The 10th percentile is the player this entire document exists to protect; if it does not move, the document failed. |
| **E2** | New assertion: **the p10 player reaches every milestone in `01` §7 within 1.35× the p50 player's time.** Without pity this ratio is unbounded. |
| **E3** | New assertion: **no pity guarantee fires for more than 30% of grants in its class** at p50. A guarantee that fires most of the time is not a safety net — it is the drop rate, and the drop table should be rewritten instead. |
| **E4** | Model Focus (§5) and Set Tokens (§5.1) explicitly: report the p50 and p90 time to a **6-piece SS set**, with and without Focus. |
| **E5** | Re-run every existing assertion. Pity raises the floor without raising the mean, but Reforge, Retune, Set Tokens and Beast Marks are all new **sinks** and will move Merge Dust and Enhance Stone balances materially. |

---

## 11. Implementation notes

- One service, `LuckService`, owns every counter. Grants do not read or write counters directly; they call `LuckService.Resolve(sourceClass, baseTable, playerCounters, rng)` and receive a resolved outcome plus a counter delta. **There must be exactly one place in the codebase where a guarantee can fire.**
- `LuckService` lives in `SlayIdleRepeat.Core` (`14` §4). It takes counters as an argument and returns deltas; it holds no state and touches no clock, so it is unit-testable and deterministic like everything else in `Core`.
- Counter storage is a single JSONB column on the player profile, keyed by the counter keys in §3.
- The architecture test suite gains one rule: **any code path that produces a gear item, pet, mount or draft option must route through `LuckService`.** A drop table consulted directly is a bug, and it is the kind of bug that only shows up in a player's 300th chest.
- Every rule in §4 gets an explicit unit test asserting the guarantee fires at exactly `N`, and a property test asserting it never fires later than `N` across 100,000 seeded sequences.

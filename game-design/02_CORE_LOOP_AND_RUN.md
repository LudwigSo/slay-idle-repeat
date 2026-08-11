# 02 — Core Loop & Run Structure

🔒 LOCKED: Run-based only. There is **no** idle income, **no** offline accrual, **no** AFK stage. If the app is closed, nothing accumulates except Energy regeneration (see `10_ECONOMY_AND_PROGRESSION.md` §3).

---

## 1. Run definition

A **Run** is one attempt at one `(Chapter, DifficultyTier)` pair.

| Property | Value |
|---|---|
| Energy cost | 20 📐 TUNABLE |
| Target duration | 8–12 min (Normal), 10–15 min (Heroic/Mythic) |
| Board length | 3 stages: 12 / 14 / 16 tiles + 1 boss tile = **43 tiles** 📐 TUNABLE |
| Expected battles per run | 18–22 (enemy + elite + boss) |
| Expected perk drafts per run | equal to battles won |
| End conditions | Boss defeated (**Victory**) · Hero HP reaches 0 (**Death**) · Player forfeits (**Abandon**) |
| Persistence | Run state lives on the server for 48 h. Closing the app, losing connection or switching device resumes the run exactly. See `14` §3. |

### 1.1 Run state machine

```
        ┌──────────────┐
        │  RUN_SETUP   │  choose chapter + tier, confirm energy, load loadout
        └──────┬───────┘
               ↓
        ┌──────────────┐
   ┌───▶│  AWAIT_ROLL  │◀──────────────────────────┐
   │    └──────┬───────┘                           │
   │           ↓ player taps ROLL                  │
   │    ┌──────────────┐                           │
   │    │   ROLLING    │  die animation, ~0.8 s    │
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   │    │ REROLL_PROMPT│  optional, if charges > 0 │
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   │    │    MOVING    │  hop N tiles, ~0.25 s each│
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   │    │ RESOLVE_TILE │  branch by TileType       │
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   │    │ (BATTLE) →   │  see 05_COMBAT_SIMULATION │
   │    │ (PERK_DRAFT) │  see 06_PERKS             │
   │    │ (SHOP/EVENT/ │                           │
   │    │  MINIGAME…)  │                           │
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   └────┤ CHECK_STATE  │ alive & tiles remain? ────┘
        └──────┬───────┘
               ↓ boss dead OR hp<=0
        ┌──────────────┐
        │ DEATH_PROMPT │  offer ad-revive (once/run) → back to AWAIT_ROLL
        └──────┬───────┘
               ↓
        ┌──────────────┐
        │ RUN_RESULTS  │  reward tally, ad-double offer, bank to save
        └──────────────┘
```

### 1.2 Stage transitions

Reaching the last tile of a stage triggers a **Stage Gate**:

- Full-screen banner: *"STAGE 2 — The Ashen Mire"*
- Hero restores **15%** of Max HP 📐 TUNABLE
- Reroll charges refresh to base value
- Perk draft rarity weights shift upward (see `06_PERKS.md` §4)
- Enemy power multiplier steps up (see §4 below)
- Autosave checkpoint written

Stage gates are the pacing heartbeat of the run. They are also the natural place for a non-intrusive interstitial ad for non-payers — see `12_MONETIZATION_ADS.md` §6.

---

## 2. Run setup

On the Chapter Select screen the player picks:

1. **Chapter** (1–8, unlocked linearly by clearing the previous chapter on Normal)
2. **Difficulty Tier** (Normal always; Heroic unlocked by clearing Normal; Mythic by clearing Heroic **and** reaching Legend Level 60 — see `10` §7)
3. Confirms their **Loadout**: equipped gear (6 slots), equipped pets (up to 3), equipped mount (1). Loadout is changed on the Hero screen, not here.

The run then generates a **Run Seed** — computed *inside* `GameRules.Apply` when it handles `START_RUN`, deterministically, from values already on the command, the state and the context (ruled in `16` A7):

```
runSeed = Hash64(playerId, chapterId, tierId, utcUnixSeconds, runCounter)

  utcUnixSeconds = floor(GameContext.NowUtc as Unix seconds)
  runCounter     = the player's lifetime runs-started counter, incremented by every
                   START_RUN — so two runs begun in the same second still differ
```

`Hash64` is the project's one pinned hash — **xxHash64** over a canonical byte encoding, defined normatively in `14` §8.0. The same function then derives every random draw in the run: draw `i` of stream `s` is `Hash64(runSeed, s, i)`, and the per-stream **draw counters** are persisted as authoritative run state (`14` §8.1). This makes runs reproducible for bug reports and makes anti-cheat validation possible.

🔒 The `runSeed` is server-side state and is **never sent to the client**. A client that held it could read tomorrow's draft options and drops — an information cheat even though outcomes are unspoofable. The client receives outcomes, the stream counters, and per-battle `battleSeed`s (`14` §8.1).

**Anti-reroll rule:** the seed is committed *before* the board is shown. The player cannot abandon and re-enter to fish for a better board without paying the Energy cost again.

---

## 3. Turn structure in detail

One "turn" = one die roll and its full resolution.

| Step | Duration | Player input |
|---|---|---|
| 1. Roll | 0.8 s | Tap the die (or tap-and-hold to see face preview) |
| 2. Reroll decision | up to 4 s, skippable | Optional tap. Costs 1 Reroll Charge. Ad-reroll available (2/run). |
| 3. Movement | 0.25 s × pips | None (auto). Tap to speed up. |
| 4. Fork choice | up to 6 s per junction | Whenever the move reaches a junction, movement pauses for `CHOOSE_FORK`; remaining pips continue down the chosen branch. May occur mid-move, more than once per move. `03` §1.1 is the authority. |
| 5. Tile resolution | 2 s – 40 s | Depends on tile type |
| 6. Perk draft | up to 15 s | Choose 1 of 3. Reroll available. |

**Speed controls:** the player can set battle speed to ×1 / ×2 / ×3 and toggle "Auto-advance" which auto-confirms non-choice screens. Auto-advance never auto-picks perks — the draft is the game's main decision and must always be manual.

📐 TUNABLE: All durations above.

---

## 4. Difficulty and power targets

Difficulty is expressed as a single scalar, **Power**, so that content authoring stays simple.

### 4.1 Chapter power targets

```
ChapterPowerTarget(c) = 1000 * 2.0^(c-1)      // c = 1..8
```

| Chapter | Power target |
|---|---|
| 1 | 1,000 |
| 2 | 2,000 |
| 3 | 4,000 |
| 4 | 8,000 |
| 5 | 16,000 |
| 6 | 32,000 |
| 7 | 64,000 |
| 8 | 128,000 |

### 4.2 Difficulty tier multipliers

| Tier | Enemy power × | Reward × | Unlock |
|---|---|---|---|
| Normal | 1.0 | 1.0 | default |
| Heroic | 4.0 | 2.5 | clear the chapter on Normal |
| Mythic | 16.0 | 6.0 | clear the chapter on Heroic **and** Legend Level 60 |

### 4.3 In-run ramp

Within a run, enemy power ramps with the node's linear index `i` (0-based, across the whole board; a branch node takes the spine-parallel index defined in `03` §1.1, so a fork is never a power discount — ruled in `16` A7):

```
EnemyPower(i) = ChapterPowerTarget(c) * TierMult(t) * (1 + 0.035 * i) * StageMult(s)

StageMult:  Stage1 = 1.00, Stage2 = 1.15, Stage3 = 1.35, Boss = 2.20
```

Enemy stat derivation from Power is defined in `05_COMBAT_SIMULATION.md` §6.

### 4.4 Player Power

⚠️ **Superseded.** `PlayerPower` is no longer a display-only aggregate — it is the canonical scalar the whole game and the economy simulator are tuned against. **`29_POWER_MODEL.md` §2 is the authority.**

The formula changed shape:

```
PlayerPower = K_POWER * sqrt(EffectiveHP * DPS)        // 29 §2.1
```

The previous additive form (`EffectiveHP * 0.5 + DPS * 10`) was **replaced because it is not monotone in both terms**: a build with enormous EffectiveHP and near-zero DPS scored highly, and under the 90-second fight cap (`05` §3) that build loses every fight it enters. A power number that recommends Chapter 6 to a build that cannot kill anything is worse than no number at all — and the simulator acts on it. The geometric mean goes to zero when either term does, which is the correct shape for *"time-to-kill must beat time-to-die"*.

The additive form is retained in `data/tuning/power_model.json` as `additive_legacy` so the change is reversible without a client patch, per this section's original requirement. It is not the default.

Both `EffectiveHP` and `DPS` are evaluated against a **fixed reference opponent** (`29` §2.2), which makes `PlayerPower` an absolute scalar: the same build scores the same number in Chapter 1 and Chapter 8.

**The warning threshold is unchanged:** if `PlayerPower < 0.7 × ParPower(chapter, tier)`, show a soft warning on the chapter confirm dialog. Never block the player — let them try. `ParPower` is the authored table in `29` §4, and §4.1–4.2 above are its default fill.

📐 TUNABLE — every weight, and now every `ParPower` cell independently.

---

## 5. Run rewards

### 5.1 Reward sources during a run

| Source | Drops |
|---|---|
| Normal enemy | Gold (run-local), small Legend XP |
| Elite enemy | Gold, Legend XP, **1 guaranteed gear item** |
| Boss | Gold, large Legend XP, **Soul Shards**, gear (rarity-weighted), chapter-first-clear bonus |
| Treasure tile | Crowns, Enhance Stones, Merge Dust |
| Minigame tile | Variable: Gold, Crowns, Beast Feed, or a perk |
| Event tile | Variable, sometimes a choice with a cost |

🔒 All in-run payout amounts — Gold per kill, run-completion Crowns, treasure, cache (including the base Pet Egg rate) and shrine buffs — are authored in **`03` §7a** (single source of truth, `data/tuning/currencies.json`; ruled in `16` A7). Legend XP amounts stay in §5.1a below.

**Gold** is run-local and vanishes at run end. It exists only to be spent at Shop tiles. This keeps in-run economy decisions crisp and prevents "hoard gold, never spend" behaviour.

### 5.1a Legend XP income 🔒

Previously stated only as "small / large XP"; these are the placeholder values, 📐 TUNABLE in `data/tuning/progression.json` and validated by the simulator (`21`).

```
BaseXp(c) = 25 × 1.55^(c-1)          // c = chapter
TierXpMult: Normal 1.0 · Heroic 1.6 · Mythic 2.5
```

| Source | Legend XP |
|---|---|
| Normal enemy kill | `1 × BaseXp(c) × TierXpMult` |
| Elite kill | `3 ×` |
| Boss kill | `15 ×` |
| Run victory bonus | `10 ×` |
| Resource Dungeon | `0.20 ×` the chapter equivalent (`25` §4.4) |

XP is a banked reward and is subject to the `CompletionMultiplier` (§5.2) and `AD_DOUBLE_LEGEND_XP`. Sanity check: a Chapter 1 Normal victory pays ≈ 1,300 XP; ~4 first-day runs reach Legend Level 9–10, matching the "PvP unlocked day 1, ~45 min" target in `01` §7.

### 5.2 Run-end payout

```
FinalPayout = BankedRewards * CompletionMultiplier * AdDoubleMultiplier

CompletionMultiplier:
    Victory (boss killed)      = 1.00
    Death in Stage 3           = 0.60
    Death in Stage 2           = 0.40
    Death in Stage 1           = 0.25
    Abandon                    = 0.10   (and no gear drops are kept)

AdDoubleMultiplier = 2.0 if the player watches the run-end rewarded ad, else 1.0
                     (auto-applied for Slay Plus subscribers)
```

🔒 **Death must still pay.** A dead run that yields nothing is the single fastest way to make a grind feel hostile. Even a Stage-1 death returns 25% and always returns any gear already picked up during the run.

### 5.3 First-clear bonuses

Each `(Chapter, Tier)` pair pays a one-time First Clear bonus: a fixed Soul Shard grant, a guaranteed gear item one rarity above the normal chapter cap, and (for Normal tier) the unlock of the next chapter.

---

## 6. Death, revive and the ad contract

On reaching 0 HP:

1. Time freezes on the battle screen; a "DEFEATED" overlay slides in.
2. If the player has not yet revived this run, offer **Revive** (see `12_MONETIZATION_ADS.md`, placement `AD_REVIVE`):
   - Restores 50% Max HP 📐 TUNABLE
   - Grants 2 s of invulnerability on resume
   - The battle **restarts from the beginning of that battle**, not mid-fight — this keeps the deterministic sim clean.
   - Limit: **once per run**, hard.
   - Slay Plus subscribers get a "REVIVE" button that resolves instantly with no ad.
3. If declined or already used, go to `RUN_RESULTS` with the Death completion multiplier.

🔒 **Revive works everywhere, including bosses.** The boss is exactly where a player most wants the safety net, it is the highest-value rewarded-ad moment in the game, and dying at 5% boss HP with no recourse is the kind of moment that ends sessions. The cost is slightly deflated boss tension, which is accepted.

---

## 7. Session flow (outside a run)

```
APP LAUNCH
   ↓
[Splash → auth → server session + profile fetch → content hash check]
   ↓
HOME / CAMP  ────────────────────────────────────────────┐
   │  shows: hero portrait+gear, Energy bar, Legend Level │
   │  bar, daily quest strip, PvP rank chip, ad-offer     │
   │  buttons, chapter "Continue" CTA                     │
   ├─▶ HERO      (equip gear, pets, mount, view stats)    │
   ├─▶ FORGE     (merge, enhance, salvage gear)           │
   ├─▶ TALENTS   (spend Talent Points)                    │
   ├─▶ MENAGERIE (pets & mounts: level, ascend, equip)    │
   ├─▶ ARENA     (Ghost Duel, ladder, season rewards)     │
   ├─▶ SHOP      (earned currency only + Slay Plus)  │
   └─▶ CHAPTER SELECT ─▶ RUN ─▶ RUN RESULTS ─▶ back to HOME
```

A returning player's ideal first 20 seconds: open app → see Energy full and 2 daily quests claimable → tap Continue → be in a run. Never more than **two taps from launch to rolling a die**.

---

## 8. First-time user experience (FTUE)

The first run is a scripted tutorial board, shipped as an **authored data package** — `ftue.json`: a fixed 12-tile layout, an ordered forced die sequence, and tutorial-only enemy, shop and draft definitions (`19` Part D, ruled in `16` A7). It is short (12 tiles, no stage gates) and it teaches exactly four things, one at a time, with no text walls:

| Step | Tile | Teaches | `19` Part D beats |
|---|---|---|---|
| 1 | Roll #1 → Enemy | Roll to move; combat is automatic; you watch | 1 |
| 2 | After battle #1 | The perk draft — the game's core decision | 2–3 |
| 3 | Roll #3 → Treasure | Loot exists and is yours | 4 |
| 4 | Roll #5 → Shop | Gold is spent inside the run | 5 |
| 5 | Roll #7 → Elite (scripted near-death), then the reroll taught on the next roll | Reroll charges; the tension of the die | 6–6b |
| 6 | Final tile → mini-boss | The victory payoff and the reward screen | 7–8 |

`19` Part D's beat numbering (0–10 plus 6b) is canonical — it is what `beatId` persistence (`19` D7) and the resume rule index. The rows above are a teaching summary, not a second numbering.

After the tutorial run: force one gear equip, one talent point spend, then release the player. **Total FTUE ≤ 5 minutes.** No ads are shown during FTUE, and Slay Plus is never surfaced before Legend Level 8.

✅ **The full FTUE package is specified in `19_CONTENT_TABLES.md` Part D** — the `ftue.json` layout, the forced die sequence, tutorial-only definitions, the scripted payout, skip semantics and per-beat resume. Two rules worth knowing from here: **skip grants the full scripted payout and jumps to beats 9–10**, so skippers and completers converge on one day-1 state; and **FTUE progress persists per beat**, so an app kill resumes at the current beat (`19` Part D7). Note one decision the script contains: there is **no tutorial narrator character**. Instructions appear as short diegetic captions on the board itself. A talking guide would have been the only recurring character in a game with no story, setting an expectation nothing else meets.

---

## 9. Retention hooks (all non-monetary)

| Hook | Cadence | Reward |
|---|---|---|
| Daily Quests | 3/day, reroll 1 | Crowns, Enhance Stones, Energy |
| Daily Login Calendar | 28-day cycle | Escalating; day 7/14/21/28 give pets or S-tier gear chests — fully authored in `19` Part G |
| Lucky Wheel | daily | 1 free spin + 2 via ad. 8 always-positive segments — see `19` Part F |
| Weekly Chapter Challenge | weekly | A modifier-loaded run (e.g. "no shops, double drops") for Soul Shards |
| PvP Season | 14 days | Soul Shards, Honor, gear chests, Talent Points |
| Codex / Bestiary completion | ongoing | Permanent tiny stat bonuses for cataloguing enemies, perks, gear |
| **Resource Dungeons** (`25`) | 9 entries/day | Deterministic Enhance Stones, Beast Feed, Crowns. The "I know exactly what I'm getting" errand. |
| **Live event** (`26`) | always one running | Event currency → a milestone track and an event shop. 14-day windows, no gaps. |
| **Guild Quests** (`27`) | daily | A collective goal and a Guild Chest for everyone who contributed anything |
| **Guild Boss** (`27`) | weekly, Mon–Sun | 3 free attempts, damage brackets — every week pays, scaled to effort |

The last four exist because the original list ran thin between day 30 and day 90. `16` **R8** records that the true end-of-content wall at Chapter 8 Mythic is still unsolved and belongs to ascension.

✅ **14 Weekly Challenge modifiers are authored in `19_CONTENT_TABLES.md` Part C**, with combination rules. The 20-quest daily pool is in Part B of the same document.
